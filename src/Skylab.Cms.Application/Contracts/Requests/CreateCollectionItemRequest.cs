using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Requests;

public sealed record CreateCollectionItemRequest(
    JsonNode Data
);
