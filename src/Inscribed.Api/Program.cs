using System.Text.Json.Serialization;
using Inscribed.Api.Endpoints;
using Inscribed.Api.Middleware;
using Inscribed.Api.Startup;
using Inscribed.Application;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Issuer;
using Inscribed.Auth.Options;
using Inscribed.Infrastructure;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInscribedAuth(builder.Configuration);

if (builder.Configuration.ReadAuthMode() is AuthMode.BuiltIn)
{
    builder.Services.AddInscribedAuthIssuer(builder.Configuration);
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ContentRead", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CapabilityCatalog.ContentRead, CapabilityCatalog.ContentWrite);
    })
    .AddPolicy("ContentWrite", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CapabilityCatalog.ContentWrite);
    })
    .AddPolicy("SchemaSync", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CapabilityCatalog.SchemaSync);
    })
    .AddPolicy("ClientAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CapabilityCatalog.ClientAdmin, CapabilityCatalog.ServiceAdmin);
        policy.RequireClaim("email");
    })
    .AddPolicy("ServiceAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(CapabilityCatalog.ServiceAdmin);
        policy.RequireClaim("email");
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Every editor request carries Authorization and so needs a preflight;
        // without a max-age browsers forget the answer after 5 seconds and pay
        // the extra round trip on nearly every call. Two hours is Chrome's cap.
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(2));
    });
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

var runMigrationsAndExit = (Environment.GetEnvironmentVariable("RUN_MIGRATIONS_AND_EXIT") ?? string.Empty).Trim().ToLowerInvariant() is "true" or "1";

if (runMigrationsAndExit)
{
    using var migrateScope = app.Services.CreateScope();
    DatabaseMigrator.MigrateAll(migrateScope.ServiceProvider);
    app.Logger.LogInformation("Database migrations applied; exiting (RUN_MIGRATIONS_AND_EXIT).");
    return;
}

using (var scope = app.Services.CreateScope())
{
    if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
        DatabaseMigrator.MigrateAll(scope.ServiceProvider);
    else
        DatabaseMigrator.EnsureUpToDate(scope.ServiceProvider);

    foreach (var module in scope.ServiceProvider.GetServices<IAuthIssuerModule>())
    {
        await module.InitializeAsync(scope.ServiceProvider);
    }

    scope.ServiceProvider.SeedInscribedClients();

    await scope.ServiceProvider.GetRequiredService<CollectionSeeder>().SeedAsync();
}

app.UseExceptionHandler();
// Content reads only, where the payloads are (a whole site per language, list
// windows). `/auth` and `/admin` answer with tokens and keys, and compressing a
// secret in the same response as caller-supplied input is what BREACH exploits.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/cms"), branch => branch.UseResponseCompression());
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapDiagnosticsEndpoints();
app.MapCmsEndpoints();
app.MapCollectionEndpoints();
app.MapClientEndpoints();
app.MapInscribedAuthEndpoints();

app.Run();
