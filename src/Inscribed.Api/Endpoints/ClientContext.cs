using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Api.Endpoints;

public static class ClientContext
{
    private const string ItemKey = "inscribed.client";

    public static RouteHandlerBuilder RequireRegisteredClient(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(ResolveAsync);

    public static RouteGroupBuilder RequireRegisteredClient(this RouteGroupBuilder builder) =>
        builder.AddEndpointFilter(ResolveAsync);

    public static Client GetClient(this HttpContext context) =>
        context.Items[ItemKey] as Client
        ?? throw new InvalidOperationException("No client resolved; RequireRegisteredClient must run before this handler.");

    private static async ValueTask<object?> ResolveAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var clientKey = http.User.GetClientId();

        if (string.IsNullOrWhiteSpace(clientKey))
            return Results.Unauthorized();

        var clients = http.RequestServices.GetRequiredService<IClientRepository>();
        var client = await clients.GetByKeyAsync(clientKey, http.RequestAborted);

        if (client is null || !client.IsActive)
            throw new UnauthorizedAccessException($"Client '{clientKey}' is not registered. Register it with POST /admin/clients first.");

        http.Items[ItemKey] = client;

        return await next(context);
    }
}
