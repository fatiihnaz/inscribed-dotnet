using System.Text.Json.Serialization;
using Inscribed.Api.Endpoints;
using Inscribed.Api.Middleware;
using Inscribed.Api.Startup;
using Inscribed.Application;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth;
using Inscribed.Auth.Endpoints;
using Inscribed.Auth.Services;
using Inscribed.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInscribedAuth(builder.Configuration);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CmsAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("cms:access");
    })
    .AddPolicy("CmsRead", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("cms:read", "cms:access");
    })
    .AddPolicy("AdminAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(builder.Configuration["Auth:Admin:Role"] ?? "cms:admin");
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
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

    scope.ServiceProvider.GetRequiredService<ISigningKeyStore>().GetPublicJwks();
    scope.ServiceProvider.GetRequiredService<ICollectionPolicyResolver>();
    scope.ServiceProvider.SeedInscribedAuth();
}

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapCmsEndpoints();
app.MapCollectionEndpoints();
app.MapInscribedAuthEndpoints();

app.Run();