using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Requests;

public sealed record ManifestBlockItem(
    string BlockPath,
    string BlockType,
    JsonNode DefaultValue,
    int SortOrder
);