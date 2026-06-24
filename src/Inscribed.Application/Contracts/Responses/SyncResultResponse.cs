namespace Inscribed.Application.Contracts.Responses;

public sealed record SyncResultResponse(
    IReadOnlyList<SyncSlugResult> Results,
    IReadOnlyList<string> PrunedSlugs
);
