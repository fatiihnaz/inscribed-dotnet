namespace Inscribed.Auth.Options;

public enum AuthMode
{
    BuiltIn,
    External,
}

public sealed class AuthOptions
{
    public AuthMode Mode { get; set; } = AuthMode.BuiltIn;

    public string Issuer { get; set; } = "http://localhost:5000";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = "inscribed-cms";

    public bool RequireHttpsMetadata { get; set; } = true;

    public string TenantClaim { get; set; } = "azp";

    public string RolesClaim { get; set; } = "roles";

    public Dictionary<string, string> RoleMap { get; set; } = [];
}
