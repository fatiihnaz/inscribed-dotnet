using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Requests;

public sealed record SaveDraftRequest(JsonNode Data);

public sealed record SaveNewDraftRequest(string? Slug, JsonNode Data);