using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Requests;

public sealed record UpsertCollectionItemRequest(
    JsonNode Data,
    int? Version,
    DateTime? PublishedAt = null,
    string? Status = null,
    string? Category = null
);
