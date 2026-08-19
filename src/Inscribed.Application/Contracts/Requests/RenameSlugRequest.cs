namespace Inscribed.Application.Contracts.Requests;

public sealed record RenameSlugRequest(
    string Slug,
    int? Version
);
