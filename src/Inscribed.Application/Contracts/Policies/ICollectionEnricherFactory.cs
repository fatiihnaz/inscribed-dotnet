namespace Inscribed.Application.Contracts.Policies;

public interface ICollectionEnricherFactory
{
    ICollectionEnricher Create(EnrichmentDefinition definition);
}
