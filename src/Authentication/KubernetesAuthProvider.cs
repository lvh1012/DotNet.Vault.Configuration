using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KubernetesAuthProvider> _logger;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;

    /// <summary>
    /// Creates a new <see cref="KubernetesAuthProvider"/> bound to the
    /// supplied options, <see cref="IHttpClientFactory"/>, and logger.
    /// </summary>
    /// <param name="options">The Kubernetes authentication options.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create Vault API clients.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public KubernetesAuthProvider(
        IOptions<KubernetesAuthenticationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<KubernetesAuthProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

        using var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultAuthClientName);
        var response = await client.PostAsync($"/v1/auth/{_options.KubernetesRolePath}/login", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();

        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);

        _logger.LogInformation(
            "Refreshed Kubernetes Vault token; lease duration {LeaseDuration}s",
            leaseDuration);
    }

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
