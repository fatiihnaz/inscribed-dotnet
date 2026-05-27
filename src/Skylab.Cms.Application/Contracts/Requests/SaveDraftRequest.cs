using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Requests;

public sealed record SaveDraftRequest(JsonNode Data);

public sealed record SaveNewDraftRequest(string? Slug, JsonNode Data);