using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Inscribed.Api.Endpoints;
using Inscribed.Api.Middleware;
using Inscribed.Application;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth;
using Inscribed.Auth.Endpoints;
using Inscribed.Auth.Services;
using Inscribed.Auth.Storage;
using Inscribed.Infrastructure;
using Inscribed.Infrastructure.Storage;

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
    db.Database.Migrate();

    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    authDb.Database.Migrate();

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