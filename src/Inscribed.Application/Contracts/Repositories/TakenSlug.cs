namespace Inscribed.Application.Contracts.Repositories;

public sealed record TakenSlug(string Slug, bool IsArchived, int Version, Guid TranslationGroupId);
