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
    private const int MaxClientKeyLength = 64;

    private static readonly Regex KeyPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex LocalePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex ClientKeyPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex FieldNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex ResponsePathPattern = new(@"^[A-Za-z0-9_]+(\[\d+\])?(\.[A-Za-z0-9_]+(\[\d+\])?)*$", RegexOptions.Compiled);

    private static readonly FieldType[] PlaceholderFieldTypes =
        [FieldType.ShortText, FieldType.LongText, FieldType.Url, FieldType.Number, FieldType.Bool, FieldType.Date, FieldType.Select];

    private static readonly FieldType[] SortableFieldTypes = [FieldType.ShortText, FieldType.Number, FieldType.Date, FieldType.Select];

    private static readonly FieldType[] ComputedFieldTypes =
        [FieldType.ShortText, FieldType.LongText, FieldType.RichText, FieldType.Url, FieldType.Bool, FieldType.Number, FieldType.Date, FieldType.StringArray, FieldType.Image, FieldType.Link];

    private static readonly FieldType[] ChoiceFieldTypes = [FieldType.Select, FieldType.StringArray];

    private static readonly Dictionary<string, FieldType> FieldTypesByName =
        Enum.GetValues<FieldType>().ToDictionary(type => type.ToString(), StringComparer.OrdinalIgnoreCase);

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
        var slugEditable = false;

        if (document.Slug is { } slug)
        {
            slugEditable = slug.Editable;

            if (slug.Editable && slug.Source == SlugSource.ClaimDerived)
                errors.Add("'slug.editable' is not valid when 'slug.source' is 'ClaimDerived'; the slug is derived from the caller claims, so renaming it would hand the item to nobody");

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

        var displayField = BuildDisplayField(document.DisplayField, fields, errors);
        var enrichments = BuildEnrichments(document.Enrich, fields, credentialNames, errors);
        var clients = BuildClients(document.Clients, errors);
        var access = BuildAccess(document.Access, document.AllowAnonymousRead, errors);

        if (errors.Count > 0 || fields is null)
            return null;

        return new FileCollectionDefinition(
            document.Key!,
            new CollectionSchema([.. fields, .. enrichments.SelectMany(ComputedFields)]),
            slugSource,
            slugSourceField,
            claimSlug,
            slugEditable,
            document.AllowAnonymousRead,
            clients,
            access,
            locales,
            source,
            enrichments);
    }

    private static string? BuildDisplayField(string? declared, List<FieldDefinition>? fields, List<string> errors)
    {
        if (declared is null)
            return null;

        if (string.IsNullOrWhiteSpace(declared))
        {
            errors.Add("'displayField' must name a field; omit the property entirely to name items by their slug");
            return null;
        }

        if (fields is null)
            return null;

        var field = fields.FirstOrDefault(candidate => string.Equals(candidate.Name, declared, StringComparison.OrdinalIgnoreCase));

        if (field is null)
        {
            errors.Add($"'displayField' references unknown field '{declared}'");
            return null;
        }

        if (field.Type is not FieldType.ShortText)
        {
            errors.Add($"'displayField' field '{declared}' must be of type ShortText, not {field.Type}; it is the one line that names a record wherever it is referenced");
            return null;
        }

        return field.Name;
    }

    private static List<string> BuildClients(List<string>? documents, List<string> errors)
    {
        if (documents is null)
            return [];

        if (documents.Count == 0)
        {
            errors.Add("'clients' must name at least one client when present; omit the key entirely to allow every client");
            return [];
        }

        var clients = new List<string>(documents.Count);

        foreach (var client in documents)
        {
            if (string.IsNullOrWhiteSpace(client))
                errors.Add("'clients' entries must not be empty");
            else if (!ClientKeyPattern.IsMatch(client))
                errors.Add($"client '{client}' must be lowercase letters, digits and hyphens, not starting or ending with a hyphen");
            else if (client.Length > MaxClientKeyLength)
                errors.Add($"client '{client}' must be at most {MaxClientKeyLength} characters");
            else if (clients.Contains(client, StringComparer.Ordinal))
                errors.Add($"client '{client}' is listed more than once");
            else
                clients.Add(client);
        }

        return clients;
    }

    private static CollectionAccess? BuildAccess(AccessDocument? document, bool allowAnonymousRead, List<string> errors)
    {
        if (document is null)
            return null;

        if (document.Read is not null && allowAnonymousRead)
            errors.Add("'access.read' cannot be combined with 'allowAnonymousRead': published data is already public, so the rule would never be consulted");

        var read = BuildRule(document.Read, "access.read", errors);
        var create = BuildRule(document.Create, "access.create", errors);
        var write = BuildRule(document.Write, "access.write", errors);

        if (read is null && create is null && write is null && errors.Count == 0)
        {
            errors.Add("'access' must declare at least one of 'read', 'create' or 'write'");
            return null;
        }

        return new CollectionAccess(read, create, write);
    }

    private static AccessRule? BuildRule(AccessRuleDocument? document, string path, List<string> errors)
    {
        if (document is null)
            return null;

        var grouped = new[] { document.All, document.Any }.Count(list => list is not null);

        if (grouped > 1)
        {
            errors.Add($"'{path}' accepts at most one of 'all' and 'any'");
            return null;
        }

        if (grouped == 0)
            return BuildLeaf(document, path, errors) is { } single ? new AccessRule(AccessCombine.All, [single]) : null;

        if (IsLeafShaped(document))
        {
            errors.Add($"'{path}' is either a single claim test or a group of them, not both");
            return null;
        }

        var combine = document.All is not null ? AccessCombine.All : AccessCombine.Any;
        var entries = document.All ?? document.Any!;
        var name = combine == AccessCombine.All ? "all" : "any";

        if (entries.Count == 0)
        {
            errors.Add($"'{path}.{name}' must contain at least one claim test");
            return null;
        }

        var leaves = new List<AccessLeaf>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            var entryPath = $"{path}.{name}[{i}]";

            if (entries[i].All is not null || entries[i].Any is not null)
            {
                errors.Add($"'{entryPath}': groups cannot be nested; every entry must be a single claim test");
                continue;
            }

            if (BuildLeaf(entries[i], entryPath, errors) is { } leaf)
                leaves.Add(leaf);
        }

        return leaves.Count == entries.Count ? new AccessRule(combine, leaves) : null;
    }

    private static AccessLeaf? BuildLeaf(AccessRuleDocument document, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(document.Claim))
        {
            errors.Add($"'{path}': 'claim' is required");
            return null;
        }

        var matchers = new object?[] { document.AnyOf, document.AllOf, document.EqualTo, document.Present }.Count(matcher => matcher is not null);

        if (matchers != 1)
        {
            errors.Add($"'{path}': declare exactly one of 'anyOf', 'allOf', 'equals' and 'present'");
            return null;
        }

        if (document.Present is { } present)
            return new AccessLeaf(document.Claim, AccessMatch.Present, [], present);

        if (document.EqualTo is { } equalTo)
        {
            if (string.IsNullOrWhiteSpace(equalTo))
            {
                errors.Add($"'{path}': 'equals' must not be empty");
                return null;
            }

            return new AccessLeaf(document.Claim, AccessMatch.Equals, [equalTo], Present: true);
        }

        var match = document.AnyOf is not null ? AccessMatch.AnyOf : AccessMatch.AllOf;
        var values = document.AnyOf ?? document.AllOf!;
        var name = match == AccessMatch.AnyOf ? "anyOf" : "allOf";

        if (values.Count == 0)
        {
            errors.Add($"'{path}': '{name}' must contain at least one value");
            return null;
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"'{path}': '{name}' entries must not be empty");
            return null;
        }

        return new AccessLeaf(document.Claim, match, values, Present: true);
    }

    private static bool IsLeafShaped(AccessRuleDocument document)
        => document.Claim is not null
            || document.AnyOf is not null
            || document.AllOf is not null
            || document.EqualTo is not null
            || document.Present is not null;

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

            var type = ResolveFieldType(document.Type, fieldRef, errors);

            List<FieldDefinition>? itemFields = null;

            if (type == FieldType.ObjectArray)
                itemFields = BuildFields(document.ItemFields, $"{path}[{i}].itemFields", errors);
            else if (type is not null && document.ItemFields is not null)
                errors.Add($"{fieldRef}: 'itemFields' is only valid for ObjectArray fields");

            var source = BuildChoiceSource(document.Source, type, fieldRef, errors);
            var mirror = BuildMirror(document, type, fieldRef, errors);

            if (document.Options is not null)
                errors.Add($"{fieldRef}: 'options' is gone; a Select or StringArray field carries its choices in 'source': {{ \"kind\": \"static\", \"values\": [...] }}");

            if (document.AllowCustom && type is { } customType && !ChoiceFieldTypes.Contains(customType))
                errors.Add($"{fieldRef}: 'allowCustom' is only valid for Select and StringArray fields; nothing else offers choices to depart from");

            if (document.Sortable && type is { } sortableType && !SortableFieldTypes.Contains(sortableType))
                errors.Add($"{fieldRef}: '{sortableType}' fields cannot be sortable; only {string.Join(", ", SortableFieldTypes)} order predictably");

            if (errors.Count > fieldErrorsBefore)
                continue;

            fields.Add(new FieldDefinition(
                document.Name!,
                type!.Value,
                string.IsNullOrWhiteSpace(document.Label) ? document.Name! : document.Label,
                Required: document.Required,
                Help: document.Help,
                ReadOnly: document.ReadOnly || mirror is not null,
                Computed: mirror is not null,
                Filterable: document.Filterable,
                Sortable: document.Sortable,
                Source: source,
                AllowCustom: document.AllowCustom,
                From: mirror,
                ItemFields: itemFields));
        }

        if (errors.Count == errorsBefore)
            ValidateMirrorTargets(fields, errors);

        return errors.Count == errorsBefore ? fields : null;
    }

    private static FieldMirror? BuildMirror(FieldDefinitionDocument document, FieldType? type, string fieldRef, List<string> errors)
    {
        if (document.From is not { } from)
            return null;

        var errorsBefore = errors.Count;

        if (string.IsNullOrWhiteSpace(from.Field))
            errors.Add($"{fieldRef}: 'from.field' is required; it names the reference this value is read through");

        if (string.IsNullOrWhiteSpace(from.Path))
            errors.Add($"{fieldRef}: 'from.path' is required; it names the field to read on the referenced item");
        else if (!FieldNamePattern.IsMatch(from.Path))
            errors.Add($"{fieldRef}: 'from.path' must be a plain field name of the referenced collection");

        if (document.Source is not null)
            errors.Add($"{fieldRef}: a field either offers choices ('source') or mirrors one ('from'), not both");

        if (type is { } declared && !ComputedFieldTypes.Contains(declared))
            errors.Add($"{fieldRef}: a mirrored field cannot be typed {declared}; only {string.Join(", ", ComputedFieldTypes)} can be read off a referenced item");

        return errors.Count == errorsBefore ? new FieldMirror(from.Field!, from.Path!) : null;
    }

    private static void ValidateMirrorTargets(List<FieldDefinition> fields, List<string> errors)
    {
        foreach (var field in fields)
        {
            if (field.From is not { } mirror)
                continue;

            var fieldRef = $"field '{field.Name}'";
            var target = fields.FirstOrDefault(candidate => string.Equals(candidate.Name, mirror.Field, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                errors.Add($"{fieldRef}: 'from.field' references unknown field '{mirror.Field}'; a mirror follows a reference declared beside it, at the same level");
                continue;
            }

            if (target.Source is not { Kind: ChoiceKind.Collection })
            {
                errors.Add($"{fieldRef}: 'from.field' must name a Select or StringArray field whose source is a collection; '{target.Name}' points at nothing to read from");
                continue;
            }

            if (target.Type == FieldType.StringArray && field.Type != FieldType.StringArray)
                errors.Add($"{fieldRef}: mirroring '{target.Name}' yields one value per reference, so the field must be typed StringArray, not {field.Type}");
        }
    }

    private static FieldType? ResolveFieldType(string? value, string fieldRef, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldRef}: 'type' is required");
            return null;
        }

        if (TryParseFieldType(value, out var type, out var error))
            return type;

        errors.Add($"{fieldRef}: {error}");
        return null;
    }

    private static bool TryParseFieldType(string value, out FieldType type, out string error)
    {
        if (FieldTypesByName.TryGetValue(value, out type))
        {
            error = string.Empty;
            return true;
        }

        error = $"unknown field type '{value}'; expected one of {string.Join(", ", FieldTypesByName.Keys)}";
        return false;
    }

    private static ChoiceSource? BuildChoiceSource(ChoiceSourceDocument? document, FieldType? type, string fieldRef, List<string> errors)
    {
        if (document is null)
        {
            if (type == FieldType.Select)
                errors.Add($"{fieldRef}: a Select field needs a 'source'; a list with nothing in it cannot be chosen from");

            return null;
        }

        if (type is { } declared && !ChoiceFieldTypes.Contains(declared))
        {
            errors.Add($"{fieldRef}: 'source' is only valid for Select and StringArray fields, not {declared}");
            return null;
        }

        if (string.Equals(document.Kind, "static", StringComparison.OrdinalIgnoreCase))
        {
            if (document.Collection is not null)
                errors.Add($"{fieldRef}: 'source.collection' is only valid when 'source.kind' is 'collection'");

            return BuildStaticSource(document.Values, fieldRef, errors);
        }

        if (string.Equals(document.Kind, "collection", StringComparison.OrdinalIgnoreCase))
        {
            if (document.Values is not null)
                errors.Add($"{fieldRef}: 'source.values' is only valid when 'source.kind' is 'static'");

            if (string.IsNullOrWhiteSpace(document.Collection))
                errors.Add($"{fieldRef}: 'source.collection' is required when 'source.kind' is 'collection'");
            else if (!KeyPattern.IsMatch(document.Collection))
                errors.Add($"{fieldRef}: 'source.collection' must be a collection key: lowercase alphanumerics separated by single hyphens");
            else
                return new ChoiceSource(ChoiceKind.Collection, Collection: document.Collection);

            return null;
        }

        errors.Add($"{fieldRef}: 'source.kind' is required and must be 'static' or 'collection'");
        return null;
    }

    private static ChoiceSource? BuildStaticSource(List<string>? values, string fieldRef, List<string> errors)
    {
        if (values is null || values.Count == 0)
        {
            errors.Add($"{fieldRef}: 'source.values' must list at least one choice when 'source.kind' is 'static'");
            return null;
        }

        var choices = new List<string>(values.Count);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{fieldRef}: 'source.values' entries must not be empty");
            else if (choices.Contains(value, StringComparer.Ordinal))
                errors.Add($"{fieldRef}: choice '{value}' is listed more than once");
            else
                choices.Add(value);
        }

        return choices.Count == values.Count ? new ChoiceSource(ChoiceKind.Static, choices) : null;
    }
}
