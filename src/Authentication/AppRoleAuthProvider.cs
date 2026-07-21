using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Authentication provider for the Vault <c>approle</c> auth method.
/// </summary>
/// <remarks>
/// Performs the <c>POST /v1/auth/{mount}/login</c> exchange with the
/// configured <see cref="AppRoleAuthenticationOptions.RoleId"/> and
/// <see cref="AppRoleAuthenticationOptions.SecretId"/>, caches the returned
/// client token, and refreshes it before its lease expires.
/// </remarks>
public class AppRoleAuthProvider : IVaultAuthenticationProvider
{
    private readonly AppRoleAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;

    /// <summary>
    /// Creates a new <see cref="AppRoleAuthProvider"/> bound to the supplied
    /// options and <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">The AppRole authentication options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault login endpoint.</param>
    public AppRoleAuthProvider(
        IOptions<AppRoleAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "approle";

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }

        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("approle", "Failed to obtain token");
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            role_id = _options.RoleId,
            secret_id = _options.SecretId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"/v1/auth/{_options.AppRolePath}/login", content, cancellationToken);
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
