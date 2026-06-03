using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Inscribed.Api.Endpoints;
using Inscribed.Api.Middleware;
using Inscribed.Application;
using Inscribed.Infrastructure;
using Inscribed.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Provider-agnostic JWT bearer pipeline. Configure "Jwt:Authority"/"Jwt:Audience"
// when an identity provider (e.g. Google OAuth, service tokens) is wired up. The
// issuer is expected to emit a "roles" claim for authorization and standard
// "sub"/"azp" claims for user and tenant identification.
var jwtSection = builder.Configuration.GetSection("Jwt");
var requireHttpsMetadata = jwtSection.GetValue("RequireHttpsMetadata", true);
var jwtAuthority = jwtSection["Authority"];
var jwtAudience = jwtSection["Audience"];
var jwtMetadataAddress = jwtSection["MetadataAddress"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        if (!string.IsNullOrWhiteSpace(jwtAuthority))
            options.Authority = jwtAuthority;
        if (!string.IsNullOrWhiteSpace(jwtAudience))
            options.Audience = jwtAudience;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(jwtMetadataAddress))
            options.MetadataAddress = jwtMetadataAddress;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CmsAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("cms:access");
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
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
}

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapCmsEndpoints();
app.MapCollectionEndpoints();

app.Run();