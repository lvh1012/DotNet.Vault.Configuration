using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNet.Vault.Configuration.Core;

/// <summary>
/// HTTP client wrapper that ties together the configured authentication providers
/// and secret backends to load secrets from a Vault server.
/// </summary>
/// <remarks>
/// <see cref="VaultClient"/> is the low-level entry point used by the configuration
/// provider. It is responsible for routing logical secret paths to the matching
/// <see cref="IVaultSecretBackend"/>, requesting tokens from the selected
/// <see cref="IVaultAuthenticationProvider"/>, and exposing the health and
/// authentication state of the underlying Vault server.
/// </remarks>
public class VaultClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VaultOptions _options;
    private readonly IEnumerable<IVaultAuthenticationProvider> _authProviders;
    private readonly IEnumerable<IVaultSecretBackend> _backends;
    private readonly ILogger<VaultClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> used to create Vault HTTP clients.</param>
    /// <param name="options">The configured <see cref="VaultOptions"/> for the target Vault server.</param>
    /// <param name="authProviders">The available authentication providers.</param>
    /// <param name="backends">The available secret backends.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public VaultClient(
        IHttpClientFactory httpClientFactory,
        VaultOptions options,
        IEnumerable<IVaultAuthenticationProvider> authProviders,
        IEnumerable<IVaultSecretBackend> backends,
        ILogger<VaultClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _authProviders = authProviders;
        _backends = backends;
        _logger = logger;
    }

    /// <summary>
    /// Loads the secrets exposed at the given logical paths, dispatching each path
    /// to the <see cref="IVaultSecretBackend"/> that owns it.
    /// </summary>
    /// <param name="paths">The logical secret paths to resolve.</param>
    /// <returns>The aggregated key/value material returned by the matching backends.</returns>
    /// <exception cref="VaultBackendNotSupportedException">
    /// Thrown when no registered backend can handle one of the supplied paths.
    /// </exception>
    public async Task<Dictionary<string, string>> LoadSecretsAsync(IEnumerable<string> paths)
    {
        var allSecrets = new Dictionary<string, string>();

        foreach (var path in paths)
        {
            try
            {
                var backend = _backends.FirstOrDefault(b => b.CanHandle(path));
                if (backend == null)
                {
                    throw new VaultBackendNotSupportedException(path);
                }

                var request = new SecretRequest { Path = path };
                var result = await backend.GetSecretsAsync(request);

                foreach (var kvp in result.Secrets)
                {
                    allSecrets[kvp.Key] = kvp.Value;
                }

                _logger.LogDebug("Loaded {Count} secrets from {Path}", result.Secrets.Count, path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets from {Path}", path);
                throw;
            }
        }

        return allSecrets;
    }

    /// <summary>
    /// Obtains a Vault client token from the authentication provider matching
    /// <see cref="VaultAuthenticationConfiguration.Method"/>.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The Vault client token.</returns>
    /// <exception cref="VaultAuthenticationException">
    /// Thrown when no authentication provider is registered for the configured method.
    /// </exception>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var provider = _authProviders.FirstOrDefault(p => p.AuthenticationMethod == _options.Authentication.Method);
        if (provider == null)
        {
            throw new VaultAuthenticationException(_options.Authentication.Method, "Authentication provider not found");
        }

        return await provider.GetTokenAsync(cancellationToken);
    }

    /// <summary>
    /// Queries the Vault server's <c>/v1/sys/health</c> endpoint and returns the parsed response.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The parsed <see cref="VaultHealthResponse"/> payload.</returns>
    /// <exception cref="VaultConnectionException">
    /// Thrown when the health endpoint cannot be reached.
    /// </exception>
    public async Task<VaultHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
            var response = await client.GetAsync("/v1/sys/health", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<VaultHealthResponse>(content, JsonSerializerOptions.Web)!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new VaultConnectionException(_options.Uri, ex);
        }
    }

    /// <summary>
    /// Indicates whether the currently held token is still accepted by the Vault server.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the <c>/v1/auth/token/lookup-self</c> call succeeds;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public async Task<bool> IsAuthenticationValidAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetTokenAsync(cancellationToken);
            var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/token/lookup-self");
            request.Headers.Add("X-Vault-Token", token);

            var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
            var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Represents the payload returned by Vault's <c>/v1/sys/health</c> endpoint.
/// </summary>
public class VaultHealthResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the Vault server has been initialized.
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Vault server is sealed.
    /// </summary>
    public bool Sealed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this node is a standby node.
    /// </summary>
    public bool Standby { get; set; }

    /// <summary>
    /// Gets or sets the Vault server version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("cluster_name")]
    /// <summary>
    /// Gets or sets the Vault cluster name.
    /// </summary>
    public string ClusterName { get; set; } = string.Empty;

    [JsonPropertyName("cluster_id")]
    /// <summary>
    /// Gets or sets the Vault cluster identifier.
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("server_time_utc")]
    /// <summary>
    /// Gets or sets the server time (UTC) reported by Vault.
    /// </summary>
    public DateTimeOffset ServerTimeUtc { get; set; }
}
