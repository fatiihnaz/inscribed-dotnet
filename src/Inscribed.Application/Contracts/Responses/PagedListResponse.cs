namespace Inscribed.Application.Contracts.Responses;

public sealed record PagedListResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int Limit
);
