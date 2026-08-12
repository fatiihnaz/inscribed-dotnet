using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Responses;

public static class VirtualItemOrigin
{
    public const string Pending = "pending";

    public const string Derived = "derived";
}

public sealed record VirtualItemResponse(
    string CollectionKey,
    string Origin,
    JsonNode Data,
    bool CanEdit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Slug = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? DraftData = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Locale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TranslationGroupId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? CreatedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? UpdatedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsArchived = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Version = null
);

public sealed record ArchiveResponse(string CollectionKey, string Slug, int Version);

public sealed record CollectionListResponse(
    IReadOnlyList<CollectionItemResponse> Items,
    int Total,
    int Offset,
    int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<VirtualItemResponse>? VirtualItems = null
);
