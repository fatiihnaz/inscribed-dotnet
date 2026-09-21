using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Responses;

public sealed record ContentPageResponse(
    string Slug,
    IReadOnlyList<BlockResponse> Blocks
);

public sealed record ContentBundleResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Locale,
    IReadOnlyList<ContentPageResponse> Global,
    IReadOnlyList<ContentPageResponse> Pages
);
