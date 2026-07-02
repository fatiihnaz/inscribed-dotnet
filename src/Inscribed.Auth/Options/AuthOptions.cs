namespace Inscribed.Auth.Options;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "http://localhost:5000";

    public string Audience { get; set; } = "inscribed-cms";

    public int AccessTokenMinutes { get; set; } = 15;
}
