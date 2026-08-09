using Inscribed.Application.Contracts.Responses;

namespace Inscribed.Application.Services;

public interface IClientService
{
    Task<ClientResponse> CreateAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, CancellationToken cancellationToken = default);

    Task<ClientResponse> UpdateAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, bool? isActive, bool? allowAnonymousContentRead, CancellationToken cancellationToken = default);

    Task<ClientResponse> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SetLocalesAsync(string key, IReadOnlyList<string> locales, CancellationToken cancellationToken = default);
}
