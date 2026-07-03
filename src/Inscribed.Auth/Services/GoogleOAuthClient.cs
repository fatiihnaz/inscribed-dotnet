using System.Text.Json;
using Inscribed.Auth.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Inscribed.Auth.Services;

internal interface IGoogleOAuthClient
{
    string BuildAuthorizationUrl(string state, string codeChallenge);
    Task<GoogleUser?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default);
}

internal sealed record GoogleUser(string Subject, string Email, bool EmailVerified, string DisplayName);

internal sealed class GoogleOAuthClient : IGoogleOAuthClient
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthOptions _options;

    public GoogleOAuthClient(IHttpClientFactory httpClientFactory, IOptions<AuthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    private string RedirectUri => _options.Issuer.TrimEnd('/') + _options.Google.CallbackPath;

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        if (string.IsNullOrWhiteSpace(_options.Google.ClientId) || string.IsNullOrWhiteSpace(_options.Google.ClientSecret))
        {
            throw new InvalidOperationException("Auth:Google:ClientId and Auth:Google:ClientSecret must be configured.");
        }

        return QueryHelpers.AddQueryString(AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = _options.Google.ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });
    }

    public async Task<GoogleUser?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        var http = _httpClientFactory.CreateClient(nameof(GoogleOAuthClient));
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.Google.ClientId,
            ["client_secret"] = _options.Google.ClientSecret,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
        }), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("id_token", out var idTokenElement) || idTokenElement.GetString() is not { } rawIdToken)
        {
            return null;
        }

        var idToken = new JsonWebToken(rawIdToken);
        if (idToken.Issuer is not ("https://accounts.google.com" or "accounts.google.com"))
        {
            return null;
        }

        if (!idToken.Audiences.Contains(_options.Google.ClientId) || idToken.ValidTo < DateTime.UtcNow)
        {
            return null;
        }

        if (!idToken.TryGetPayloadValue<string>("email", out var email) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var emailVerified = idToken.TryGetPayloadValue<bool>("email_verified", out var verified) && verified;
        var displayName = idToken.TryGetPayloadValue<string>("name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : email;

        return new GoogleUser(idToken.Subject, email, emailVerified, displayName);
    }
}