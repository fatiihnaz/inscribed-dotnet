using System.Text.Json.Nodes;
using Inscribed.Domain.Enums;

namespace Inscribed.Application.Contracts.Requests;

public sealed record ManifestBlockItem(
    string BlockPath,
    BlockType BlockType,
    JsonNode DefaultValue,
    int SortOrder
);