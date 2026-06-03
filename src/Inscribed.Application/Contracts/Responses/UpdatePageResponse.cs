namespace Inscribed.Application.Contracts.Responses;

public sealed record UpdatePageResponse(
    int Updated,
    int Unchanged
);