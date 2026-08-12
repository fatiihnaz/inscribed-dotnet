using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Responses;

public sealed record TranslationRef(string? Locale, string Slug);

public sealed record CollectionItemResponse(
    Guid Id,
    string CollectionKey,
    string? Slug,
    JsonNode Data,
    int Version,
    Guid TranslationGroupId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Locale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TranslationRef>? Translations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanEdit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? DraftData = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsArchived = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ArchivedAt = null
);
