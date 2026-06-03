using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Requests;

public sealed record UpdateBlockItem(
    string BlockPath,
    JsonNode Value,
    int Version
);