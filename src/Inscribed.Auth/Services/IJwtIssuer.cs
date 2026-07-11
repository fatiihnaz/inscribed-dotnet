namespace Inscribed.Auth.Services;

public interface IJwtIssuer
{
    AccessToken Issue(string subject, string clientKey, IReadOnlyList<string> roles, string displayName, string email);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);