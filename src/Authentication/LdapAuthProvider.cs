using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Authentication provider for the Vault <c>ldap</c> auth method.
/// </summary>
/// <remarks>
/// Performs the <c>POST /v1/auth/{mount}/login/{username}</c> exchange with the
/// configured <see cref="LdapAuthenticationOptions.Username"/> and
/// <see cref="LdapAuthenticationOptions.Password"/>, caches the returned client
/// token, and refreshes it before its lease expires.
/// </remarks>
public class LdapAuthProvider : IVaultAuthenticationProvider
{
    private readonly LdapAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;

    /// <summary>
    /// Creates a new <see cref="LdapAuthProvider"/> bound to the supplied
    /// options and <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">The LDAP authentication options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault login endpoint.</param>
    public LdapAuthProvider(
        IOptions<LdapAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "ldap";

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }

        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("ldap", "Failed to obtain token");
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new { password = _options.Password };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"/v1/auth/{_options.LdapPath}/login/{_options.Username}", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();

        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
