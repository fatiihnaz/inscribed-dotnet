using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Responses;

public sealed record ContentResponse(
    string Slug,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Locale,
    IReadOnlyList<BlockResponse> Blocks
);
