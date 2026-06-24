namespace Inscribed.Application.Contracts.Responses;

public sealed record SyncSlugResult(
    string Slug,
    int Created,
    int Deleted,
    int Unchanged,
    int Restored
);
