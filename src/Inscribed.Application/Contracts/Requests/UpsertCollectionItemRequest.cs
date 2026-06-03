using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Requests;

public sealed record UpsertCollectionItemRequest(
    JsonNode Data,
    int? Version
);
