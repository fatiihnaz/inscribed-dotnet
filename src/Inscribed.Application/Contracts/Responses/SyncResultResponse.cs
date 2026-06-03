namespace Inscribed.Application.Contracts.Responses;

public sealed record SyncResultResponse(
    int Created,
    int Deleted,
    int Unchanged
);