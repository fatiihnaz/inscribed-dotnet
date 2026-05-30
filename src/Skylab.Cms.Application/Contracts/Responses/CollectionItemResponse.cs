using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Skylab.Cms.Application.Contracts.Responses;

public sealed record CollectionItemResponse(
    Guid Id,
    string CollectionKey,
    string? Slug,
    JsonNode Data,
    int Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanEdit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? DraftData = null
);
