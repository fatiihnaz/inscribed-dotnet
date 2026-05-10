using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Responses;

public sealed record BlockResponse(
    string BlockPath,
    string BlockType,
    JsonNode Value,
    int SortOrder,
    int Version,
    JsonNode? Data,
    JsonNode? DraftValue = null
);