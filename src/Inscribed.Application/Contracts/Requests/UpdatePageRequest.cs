namespace Inscribed.Application.Contracts.Requests;

public sealed record UpdatePageRequest(
    string Slug,
    IReadOnlyList<UpdateBlockItem> Blocks
);