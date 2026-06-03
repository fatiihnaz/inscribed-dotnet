namespace Inscribed.Application.Contracts.Requests;

public sealed record SyncManifestRequest(
    string Slug,
    IReadOnlyList<ManifestBlockItem> Blocks
);