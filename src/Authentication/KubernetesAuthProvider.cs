using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Authentication provider for the Vault <c>kubernetes</c> auth method.
/// </summary>
/// <remarks>
/// Reads the projected service account JWT from
/// <see cref="KubernetesAuthenticationOptions.ServiceAccountTokenPath"/> and
/// performs the <c>POST /v1/auth/{mount}/login</c> exchange on behalf of the
/// configured role. The returned client token is cached and refreshed before
/// its lease expires.
/// </remarks>
public class KubernetesAuthProvider : IVaultAuthenticationProvider
{
    private readonly KubernetesAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;

    /// <summary>
    /// Creates a new <see cref="KubernetesAuthProvider"/> bound to the
    /// supplied options and <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">The Kubernetes authentication options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault login endpoint.</param>
    public KubernetesAuthProvider(
        IOptions<KubernetesAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "kubernetes";

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }

        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("kubernetes", "Failed to obtain token");
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var jwt = await File.ReadAllTextAsync(_options.ServiceAccountTokenPath, cancellationToken);

        var payload = new
        {
            role = _options.Role,
            jwt = jwt
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"/v1/auth/{_options.KubernetesRolePath}/login", content, cancellationToken);
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
