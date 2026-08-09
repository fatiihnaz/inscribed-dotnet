namespace Inscribed.Application.Contracts.Responses;

public sealed record ClientResponse(
    Guid Id,
    string Key,
    IReadOnlyList<string> Locales,
    bool AllowAnonymousContentRead,
    bool IsActive,
    DateTime CreatedAt
);
