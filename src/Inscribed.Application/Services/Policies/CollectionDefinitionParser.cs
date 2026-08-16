using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed record CollectionDefinitionParseResult(FileCollectionDefinition? Definition, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Definition is not null;
}

public static class CollectionDefinitionParser
{
    private const int DefaultCacheSeconds = 300;
    private const int MaxCacheSeconds = 86_400;

    private static readonly Regex KeyPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex LocalePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex FieldNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex ResponsePathPattern = new(@"^[A-Za-z0-9_]+(\[\d+\])?(\.[A-Za-z0-9_]+(\[\d+\])?)*$", RegexOptions.Compiled);

    private static readonly FieldType[] PlaceholderFieldTypes = [FieldType.ShortText, FieldType.LongText, FieldType.Url, FieldType.Number, FieldType.Bool, FieldType.Date];

    private static readonly FieldType[] SortableFieldTypes = [FieldType.ShortText, FieldType.Number, FieldType.Date];

    private static readonly FieldType[] ComputedFieldTypes =
        [FieldType.ShortText, FieldType.LongText, FieldType.RichText, FieldType.Url, FieldType.Bool, FieldType.Number, FieldType.Date, FieldType.StringArray, FieldType.Image];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonNode? ReadDocument(string json, out string? error)
    {
        try
        {
            var node = JsonNode.Parse(json, documentOptions: DocumentOptions);

            if (node is null)
            {
                error = "definition is empty";
                return null;
            }

            error = null;
            return node;
        }
        catch (JsonException exception)
        {
            error = $"invalid JSON: {exception.Message}";
            return null;
        }
    }

    public static CollectionDefinitionParseResult Parse(JsonNode document, string source, IReadOnlyCollection<string> credentialNames)
    {
        ArgumentNullException.ThrowIfNull(document);

        CollectionDefinitionDocument? parsed;

        try
        {
            parsed = document.Deserialize<CollectionDefinitionDocument>(SerializerOptions);
        }
        catch (JsonException exception)
        {
            return new CollectionDefinitionParseResult(null, [$"invalid JSON: {exception.Message}"]);
        }

        if (parsed is null)
            return new CollectionDefinitionParseResult(null, ["definition is empty"]);

        var errors = new List<string>();
        var definition = BuildDefinition(parsed, source, credentialNames, errors);

        return errors.Count > 0 || definition is null
            ? new CollectionDefinitionParseResult(null, errors)
            : new CollectionDefinitionParseResult(definition, []);
    }

    private static FileCollectionDefinition? BuildDefinition(
        CollectionDefinitionDocument document,
        string source,
        IReadOnlyCollection<string> credentialNames,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(document.Key))
            errors.Add("'key' is required");
        else if (!KeyPattern.IsMatch(document.Key))
            errors.Add($"key '{document.Key}' must be lowercase alphanumerics separated by single hyphens (e.g. 'team-members')");

        var fields = BuildFields(document.Fields, "fields", errors);
        var locales = BuildLocales(document.Locales, errors);

        var slugSource = SlugSource.UserDefined;
        string? slugSourceField = null;
        ClaimSlugRule? claimSlug = null;

        if (document.Slug is { } slug)
        {
            if (slug.Source is not SlugSource.ClaimDerived
                && (slug.Claim ?? slug.EndsWith ?? slug.StartsWith ?? slug.Pattern) is not null)
            {
                errors.Add("'slug.claim', 'slug.endsWith', 'slug.startsWith' and 'slug.pattern' are only valid when 'slug.source' is 'ClaimDerived'");
            }

            if (slug.Source is null)
            {
                errors.Add("'slug.source' is required and must be 'UserDefined', 'AutoGenerated' or 'ClaimDerived'");
            }
            else if (slug.Source == SlugSource.ClaimDerived)
            {
                slugSource = SlugSource.ClaimDerived;
                claimSlug = BuildClaimSlugRule(slug, errors);
            }
            else if (slug.Source == SlugSource.AutoGenerated)
            {
                slugSource = SlugSource.AutoGenerated;

                if (string.IsNullOrWhiteSpace(slug.From))
                {
                    errors.Add("'slug.from' is required when 'slug.source' is 'AutoGenerated'");
                }
                else if (fields is not null)
                {
                    var sourceField = fields.FirstOrDefault(f => string.Equals(f.Name, slug.From, StringComparison.OrdinalIgnoreCase));

                    if (sourceField is null)
                        errors.Add($"'slug.from' references unknown field '{slug.From}'");
                    else if (sourceField.Type is not FieldType.ShortText)
                        errors.Add($"'slug.from' field '{slug.From}' must be of type ShortText, not {sourceField.Type}");
                    else
                        slugSourceField = sourceField.Name;
                }
            }
            else if (slug.From is not null)
            {
                errors.Add("'slug.from' is only valid when 'slug.source' is 'AutoGenerated'");
            }
        }

        var enrichments = BuildEnrichments(document.Enrich, fields, credentialNames, errors);

        if (errors.Count > 0 || fields is null)
            return null;

        return new FileCollectionDefinition(
            document.Key!,
            new CollectionSchema([.. fields, .. enrichments.SelectMany(ComputedFields)]),
            slugSource,
            slugSourceField,
            claimSlug,
            document.AllowAnonymousRead,
            locales,
            source,
            enrichments);
    }

    private static List<string> BuildLocales(List<string>? documents, List<string> errors)
    {
        if (documents is null)
            return [];

        if (documents.Count == 0)
        {
            errors.Add("'locales' must contain at least one locale when present; omit the key entirely for a single-language collection");
            return [];
        }

        var locales = new List<string>(documents.Count);

        foreach (var locale in documents)
        {
            if (string.IsNullOrWhiteSpace(locale))
                errors.Add("'locales' entries must not be empty");
            else if (!LocalePattern.IsMatch(locale))
                errors.Add($"locale '{locale}' must be lowercase alphanumerics separated by single hyphens (e.g. 'tr', 'pt-br')");
            else if (locales.Contains(locale, StringComparer.Ordinal))
                errors.Add($"locale '{locale}' is listed more than once");
            else
                locales.Add(locale);
        }

        return locales;
    }

    private static List<EnrichmentDefinition> BuildEnrichments(
        List<EnrichmentDocument>? documents,
        List<FieldDefinition>? fields,
        IReadOnlyCollection<string> credentialNames,
        List<string> errors)
    {
        if (documents is null || documents.Count == 0)
            return [];

        var enrichments = new List<EnrichmentDefinition>(documents.Count);
        var mapTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var entryErrorsBefore = errors.Count;
            var entryRef = $"'enrich[{i}]'";

            if (string.IsNullOrWhiteSpace(document.Url))
            {
                errors.Add($"{entryRef}: 'url' is required");
            }
            else
            {
                foreach (Match match in PlaceholderPattern.Matches(document.Url))
                {
                    var placeholder = match.Groups[1].Value;

                    if (placeholder == "slug")
                        continue;

                    if (fields is null)
                        continue;

                    var field = fields.FirstOrDefault(f => f.Name == placeholder);

                    if (field is null)
                        errors.Add($"{entryRef}: url placeholder '{{{placeholder}}}' references unknown field '{placeholder}' (names are case-sensitive)");
                    else if (!PlaceholderFieldTypes.Contains(field.Type))
                        errors.Add($"{entryRef}: url placeholder '{{{placeholder}}}' must reference a scalar field, not {field.Type}");
                }

                var probeUrl = PlaceholderPattern.Replace(document.Url, "x");

                if (!Uri.TryCreate(probeUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    errors.Add($"{entryRef}: 'url' must be an absolute http(s) URL");
            }

            if (document.Auth is { } auth && !credentialNames.Contains(auth))
                errors.Add($"{entryRef}: references unknown credential '{auth}' (define it under Enrichment:Auth)");

            var cacheSeconds = document.CacheSeconds ?? DefaultCacheSeconds;

            if (cacheSeconds is < 0 or > MaxCacheSeconds)
                errors.Add($"{entryRef}: 'cacheSeconds' must be between 0 and {MaxCacheSeconds}");

            var targets = new List<EnrichmentTarget>();

            if (document.Map is null || document.Map.Count == 0)
            {
                errors.Add($"{entryRef}: 'map' must contain at least one target field");
            }
            else
            {
                foreach (var (target, node) in document.Map)
                {
                    if (!FieldNamePattern.IsMatch(target))
                        errors.Add($"{entryRef}: map target '{target}' must start with a letter or underscore and contain only letters, digits, and underscores");
                    else if (fields is not null && fields.Any(f => string.Equals(f.Name, target, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"{entryRef}: map target '{target}' collides with a schema field");
                    else if (!mapTargets.Add(target))
                        errors.Add($"{entryRef}: map target '{target}' is already produced by another enrich entry");

                    if (BuildTarget(target, node, entryRef, errors) is { } built)
                        targets.Add(built);
                }
            }

            if (errors.Count > entryErrorsBefore)
                continue;

            enrichments.Add(new EnrichmentDefinition(
                document.Url!,
                document.Auth,
                cacheSeconds,
                targets));
        }

        return enrichments;
    }

    private static ClaimSlugRule? BuildClaimSlugRule(SlugDefinitionDocument slug, List<string> errors)
    {
        var errorsBefore = errors.Count;

        if (string.IsNullOrWhiteSpace(slug.Claim))
            errors.Add("'slug.claim' is required when 'slug.source' is 'ClaimDerived'");

        if (slug.From is not null)
            errors.Add("'slug.from' is only valid when 'slug.source' is 'AutoGenerated'");

        var matchers = new[] { slug.EndsWith, slug.StartsWith, slug.Pattern }.Count(matcher => !string.IsNullOrWhiteSpace(matcher));

        if (matchers > 1)
            errors.Add("'slug' accepts at most one of 'endsWith', 'startsWith' and 'pattern'");

        Regex? pattern = null;

        if (!string.IsNullOrWhiteSpace(slug.Pattern))
        {
            try
            {
                pattern = new Regex(slug.Pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

                if (pattern.GetGroupNumbers().Length != 2)
                    errors.Add("'slug.pattern' must contain exactly one capture group; group 1 becomes the slug");
            }
            catch (ArgumentException exception)
            {
                errors.Add($"'slug.pattern' must be a valid non-backtracking regular expression: {exception.Message}");
            }
        }

        return errors.Count == errorsBefore
            ? new ClaimSlugRule(slug.Claim!, slug.EndsWith, slug.StartsWith, pattern)
            : null;
    }

    private static IEnumerable<FieldDefinition> ComputedFields(EnrichmentDefinition enrichment) =>
        enrichment.Targets.Select(target => new FieldDefinition(target.Name, target.Type, target.Label, ReadOnly: true, Computed: true));

    private static EnrichmentTarget? BuildTarget(string target, JsonNode? node, string entryRef, List<string> errors)
    {
        string? path;
        var type = FieldType.ShortText;
        string? label = null;

        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            path = value.GetValue<string>();
        }
        else if (node is JsonObject)
        {
            MapTargetDocument? document;

            try
            {
                document = node.Deserialize<MapTargetDocument>(SerializerOptions);
            }
            catch (JsonException ex)
            {
                errors.Add($"{entryRef}: map target '{target}': {ex.Message}");
                return null;
            }

            path = document?.Path;
            label = document?.Label;

            if (document?.Type is { } declared)
            {
                if (!ComputedFieldTypes.Contains(declared))
                {
                    errors.Add($"{entryRef}: map target '{target}' cannot be typed {declared}; a map entry has no way to describe its item shape");
                    return null;
                }

                type = declared;
            }
        }
        else
        {
            errors.Add($"{entryRef}: map target '{target}' must be a response path, or an object with 'path' and optionally 'type' and 'label'");
            return null;
        }

        if (string.IsNullOrWhiteSpace(path) || !ResponsePathPattern.IsMatch(path))
        {
            errors.Add($"{entryRef}: map path '{path}' for '{target}' must be a dotted path like 'owner.avatar_url' or 'topics[0]'");
            return null;
        }

        return new EnrichmentTarget(target, path, type, string.IsNullOrWhiteSpace(label) ? target : label);
    }

    private static List<FieldDefinition>? BuildFields(List<FieldDefinitionDocument>? documents, string path, List<string> errors)
    {
        if (documents is null || documents.Count == 0)
        {
            errors.Add($"'{path}' must contain at least one field");
            return null;
        }

        var errorsBefore = errors.Count;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldDefinition>(documents.Count);

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var fieldErrorsBefore = errors.Count;
            var fieldRef = string.IsNullOrWhiteSpace(document.Name) ? $"'{path}[{i}]'" : $"field '{document.Name}'";

            if (string.IsNullOrWhiteSpace(document.Name))
                errors.Add($"{fieldRef}: 'name' is required");
            else if (!FieldNamePattern.IsMatch(document.Name))
                errors.Add($"{fieldRef}: field names must start with a letter or underscore and contain only letters, digits, and underscores");
            else if (!names.Add(document.Name))
                errors.Add($"{fieldRef}: duplicate field name");

            if (document.Type is null)
                errors.Add($"{fieldRef}: 'type' is required");

            List<FieldDefinition>? itemFields = null;

            if (document.Type == FieldType.ObjectArray)
                itemFields = BuildFields(document.ItemFields, $"{path}[{i}].itemFields", errors);
            else if (document.ItemFields is not null)
                errors.Add($"{fieldRef}: 'itemFields' is only valid for ObjectArray fields");

            if (document.Sortable && document.Type is { } sortableType && !SortableFieldTypes.Contains(sortableType))
                errors.Add($"{fieldRef}: '{sortableType}' fields cannot be sortable; only {string.Join(", ", SortableFieldTypes)} order predictably");

            if (errors.Count > fieldErrorsBefore)
                continue;

            fields.Add(new FieldDefinition(
                document.Name!,
                document.Type!.Value,
                string.IsNullOrWhiteSpace(document.Label) ? document.Name! : document.Label,
                Required: document.Required,
                Help: document.Help,
                ReadOnly: document.ReadOnly,
                Filterable: document.Filterable,
                Sortable: document.Sortable,
                Options: document.Options,
                ItemFields: itemFields));
        }

        return errors.Count == errorsBefore ? fields : null;
    }
}
