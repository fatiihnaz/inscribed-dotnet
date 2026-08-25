using Microsoft.AspNetCore.Routing;

namespace Inscribed.Auth;

public interface IAuthIssuerModule
{
    void Migrate(IServiceProvider services);

    void EnsureUpToDate(IServiceProvider services);

    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);

    void MapEndpoints(IEndpointRouteBuilder app);
}
