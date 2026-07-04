using Microsoft.AspNetCore.Http;

namespace Inscribed.Auth.Options;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "http://localhost:5000";

    public string Audience { get; set; } = "inscribed-cms";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    public string AdminClientKey { get; set; } = "admin";

    public AuthCookieOptions Cookie { get; set; } = new();

    public GoogleAuthOptions Google { get; set; } = new();

    public AdminAuthOptions Admin { get; set; } = new();
}

public sealed class AuthCookieOptions
{
    public string Name { get; set; } = "inscribed_rt";

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;

    public bool Secure { get; set; } = true;
}

public sealed class GoogleAuthOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/auth/google/callback";
}

public sealed class AdminAuthOptions
{
    public string Role { get; set; } = "cms:admin";

    public string[] BootstrapAdmins { get; set; } = [];

    public string[] ConsoleOrigins { get; set; } = [];
}
