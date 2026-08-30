using Inscribed.Auth.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Options;

internal sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    private readonly IHostEnvironment _environment;

    public AuthOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TenantClaim))
        {
            failures.Add("Auth:TenantClaim must name the claim that carries the client key.");
        }

        if (string.IsNullOrWhiteSpace(options.RolesClaim))
        {
            failures.Add("Auth:RolesClaim must name the claim that carries capabilities.");
        }

        if (options.Mode is AuthMode.External)
        {
            if (string.IsNullOrWhiteSpace(options.Authority))
            {
                failures.Add("Auth:Authority is required when Auth:Mode is External; point it at the issuer's realm, e.g. https://keycloak.example.com/realms/inscribed.");
            }
            else if (_environment.IsProduction() && options.Authority.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Auth:Authority must not be a localhost URL in Production; the API resolves it from inside its own container.");
            }
        }
        else
        {
            if (!string.Equals(options.TenantClaim, CapabilityCatalog.TenantClaim, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Auth:TenantClaim must stay '{CapabilityCatalog.TenantClaim}' when Auth:Mode is BuiltIn: the bundled issuer mints that claim itself. "
                    + "Configure a different claim only alongside an external issuer.");
            }

            if (_environment.IsProduction()
                && (string.IsNullOrWhiteSpace(options.Issuer) || options.Issuer.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add("Auth:Issuer must be set to the public URL in Production.");
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
