using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Responses;

public sealed record CollectionItemResponse(
    Guid Id,
    string CollectionKey,
    string Slug,
    JsonNode Data,
    int Version,
    DateTime? PublishedAt,
    string? Status,
    string? Category,
    bool CanEdit
);
