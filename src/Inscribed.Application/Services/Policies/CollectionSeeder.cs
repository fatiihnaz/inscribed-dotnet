using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Repositories;
using Microsoft.Extensions.Logging;

namespace Inscribed.Application.Services.Policies;

public sealed class CollectionSeeder
{
    public const string SeedAuthor = "seed";

    private readonly ICollectionDefinitionRepository _repository;
    private readonly ICollectionDefinitionAdminService _definitions;
    private readonly CollectionsPath _path;
    private readonly IReadOnlyCollection<string> _credentialNames;
    private readonly ILogger<CollectionSeeder> _logger;

    public CollectionSeeder(
        ICollectionDefinitionRepository repository,
        ICollectionDefinitionAdminService definitions,
        CollectionsPath path,
        EnrichmentCredentialNames credentialNames,
        ILogger<CollectionSeeder> logger)
    {
        _repository = repository;
        _definitions = definitions;
        _path = path;
        _credentialNames = credentialNames.Names;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _repository.AnyAsync(cancellationToken))
        {
            var onDisk = Directory.Exists(_path.Directory) ? Directory.GetFiles(_path.Directory, "*.json").Length : 0;

            if (onDisk > 0)
                _logger.LogWarning(
                    "Collection definitions already exist in the database, so the {Count} file(s) in '{Directory}' are ignored. The database is the source of truth; use 'collection import' to apply a file.",
                    onDisk, _path.Directory);

            return;
        }

        var files = CollectionDefinitionFileReader.Load(_path.Directory, _path.Required, _credentialNames);

        if (files.Count == 0)
            return;

        foreach (var file in files)
            await _definitions.ImportAsync(file.Document, file.FileName, SeedAuthor, cancellationToken: cancellationToken);

        _logger.LogInformation("Seeded {Count} collection definition(s) from '{Directory}'.", files.Count, _path.Directory);
    }
}
