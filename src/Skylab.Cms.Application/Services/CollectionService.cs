using System.Security.Claims;
using Skylab.Cms.Application.Contracts.Repositories;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Contracts.Responses;
using Skylab.Cms.Application.Services.Helpers;
using Skylab.Cms.Application.Services.Policies;
using Skylab.Cms.Domain.Entities;
using Skylab.Cms.Domain.Enums;
using Skylab.Cms.Domain.Exceptions;

namespace Skylab.Cms.Application.Services;

public sealed class CollectionService : ICollectionService
{
    private readonly ICollectionItemRepository _repository;
    private readonly ICollectionPolicyResolver _policyResolver;

    public CollectionService(ICollectionItemRepository repository, ICollectionPolicyResolver policyResolver)
    {
        _repository = repository;
        _policyResolver = policyResolver;
    }

    public Contracts.Schemas.CollectionSchema GetSchema(CollectionKey key)
        => _policyResolver.Resolve(key).Schema;

    public async Task<IReadOnlyList<CollectionItemResponse>> ListAsync(CollectionKey key, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        var items = await _repository.ListAsync(key, cancellationToken: cancellationToken);

        var responses = new List<CollectionItemResponse>(items.Count);
        foreach (var item in items)
        {
            var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
            responses.Add(ToResponse(item, enriched, policy.CanEdit(user, item.Slug)));
        }

        return responses;
    }

    public async Task<CollectionItemResponse?> GetAsync(CollectionKey key, string slug, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);
        if (item is null) return null;

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        return ToResponse(item, enriched, policy.CanEdit(user, item.Slug));
    }

    public async Task<CollectionItemResponse> UpsertAsync(CollectionKey key, string slug, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data);

        var utcNow = DateTime.UtcNow;
        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);

        if (item is null)
        {
            item = CollectionItem.Create(key, normalizedSlug, validated, updatedBy, utcNow,
                request.PublishedAt, request.Status, request.Category);
            await _repository.AddAsync(item, cancellationToken);
        }
        else
        {
            if (request.Version is { } v && v != item.Version)
                throw new ConcurrencyConflictException($"Version conflict on '{key}/{normalizedSlug}'. Expected {item.Version}, got {v}.");

            item.UpdateData(validated, updatedBy, utcNow);
            item.UpdateMetadata(request.PublishedAt, request.Status, request.Category, updatedBy, utcNow);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        return ToResponse(item, enriched, canEdit: true);
    }

    private static CollectionItemResponse ToResponse(CollectionItem item, System.Text.Json.Nodes.JsonNode data, bool canEdit) =>
        new(
            Id: item.Id,
            CollectionKey: item.CollectionKey.ToString(),
            Slug: item.Slug,
            Data: data,
            Version: item.Version,
            PublishedAt: item.PublishedAt,
            Status: item.Status,
            Category: item.Category,
            CanEdit: canEdit
        );
}