using System.Net.Http;
using System.Text.Json;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;
using DotNet.Vault.Configuration.Refresh;

namespace DotNet.Vault.Configuration.Backends;

/// <summary>
/// Vault Key/Value (KV) secret backend that supports both engine version 1
/// and version 2 payloads.
/// </summary>
/// <remarks>
/// <para>
/// KV v2 responses nest the user data inside <c>{ "data": { "data": { ... } } }</para>
/// <para>KV v1 responses expose the user data directly under <c>{ "data": { ... } }</c>.</para>
/// <see cref="GetSecretsAsync"/> normalizes both shapes into a flat
/// <see cref="SecretResult"/>.
/// </remarks>
public class KvSecretBackend : IVaultSecretBackend
{
    private readonly KvSecretBackendOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SecretRefresher? _refresher;
    private readonly IVaultAuthenticationProvider? _authProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="KvSecretBackend"/> class.
    /// </summary>
    /// <param name="options">The KV backend options.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create Vault API clients.</param>
    /// <param name="refresher">The refresher used to track secret lease metadata.</param>
    /// <param name="authProvider">The optional authentication provider used to attach an <c>X-Vault-Token</c> header to outgoing requests.</param>
    public KvSecretBackend(KvSecretBackendOptions options, IHttpClientFactory httpClientFactory, SecretRefresher refresher, IVaultAuthenticationProvider? authProvider = null)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _refresher = refresher;
        _authProvider = authProvider;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="KvSecretBackend"/> class with a supplied HTTP client.
    /// </summary>
    /// <param name="options">The KV backend options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault API.</param>
    /// <param name="authProvider">The optional authentication provider used to attach an <c>X-Vault-Token</c> header to outgoing requests.</param>
    public KvSecretBackend(KvSecretBackendOptions options, HttpClient httpClient, IVaultAuthenticationProvider? authProvider = null)
        : this(options, new SingleHttpClientFactory(httpClient), null!, authProvider)
    {
    }


    /// <inheritdoc />
    public string BackendType => "kv";

    /// <inheritdoc />
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/{request.Path}");
        if (_authProvider is not null)
        {
            var token = await _authProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                httpRequest.Headers.Add("X-Vault-Token", token);
        }

        var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
        var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(content);

        var secrets = new Dictionary<string, string>();

        if (_options.Version == 2 && result.TryGetProperty("data", out var dataElement))
        {
            if (dataElement.TryGetProperty("data", out var innerData))
            {
                foreach (var prop in innerData.EnumerateObject())
                {
                    secrets[prop.Name] = prop.Value.ToString();
                }
            }
        }
        else if (result.TryGetProperty("data", out var v1Data))
        {
            foreach (var prop in v1Data.EnumerateObject())
            {
                secrets[prop.Name] = prop.Value.ToString();
            }
        }

        var secretResult = new SecretResult
        {
            Secrets = secrets,
            LeaseDuration = null,
            Renewable = false
        };

        _refresher?.TrackSecret(request.Path, secretResult);
        return secretResult;
    }

    /// <inheritdoc />
    public bool CanHandle(string path)
    {
        return path.StartsWith(_options.BackendPath);
    }

    /// <inheritdoc />
    public TimeSpan? GetTtl(string path)
    {
        return null;
    }
}
