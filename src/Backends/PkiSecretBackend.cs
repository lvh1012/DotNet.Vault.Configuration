using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Http;

namespace DotNet.Vault.Configuration.Backends;

/// <summary>
/// Vault PKI secret backend that issues short-lived X.509 certificates
/// for the configured <see cref="PkiSecretBackendOptions.CommonName"/>.
/// </summary>
/// <remarks>
/// <see cref="GetSecretsAsync"/> issues a <c>POST</c> against
/// <c>/v1/{path}</c> with a JSON payload describing the requested
/// certificate. The returned <c>certificate</c>, <c>private_key</c>, and
/// <c>ca_chain</c> are surfaced as <c>certificate.pem</c>,
/// <c>certificate.key</c>, and <c>certificate.ca_chain</c> respectively.
/// </remarks>
public class PkiSecretBackend : IVaultSecretBackend
{
    private readonly PkiSecretBackendOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVaultAuthenticationProvider? _authProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PkiSecretBackend"/> class.
    /// </summary>
    /// <param name="options">The PKI backend options.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create Vault API clients.</param>
    /// <param name="authProvider">The optional authentication provider used to attach an <c>X-Vault-Token</c> header to outgoing requests.</param>
    public PkiSecretBackend(PkiSecretBackendOptions options, IHttpClientFactory httpClientFactory, IVaultAuthenticationProvider? authProvider = null)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _authProvider = authProvider;
    }

    /// <inheritdoc />
    public string BackendType => "pki";

    /// <inheritdoc />
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["common_name"] = _options.CommonName,
            ["alt_names"] = string.Join(",", _options.AltNames)
        };
        if (_options.Ttl.HasValue)
        {
            payload["ttl"] = _options.Ttl.Value.TotalSeconds.ToString();
        }

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/{request.Path}") { Content = content };
        if (_authProvider is not null)
        {
            var token = await _authProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                httpRequest.Headers.Add("X-Vault-Token", token);
        }

        var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
        var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var secrets = new Dictionary<string, string>();

        if (result.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("certificate", out var cert))
                secrets["certificate.pem"] = cert.GetString() ?? "";

            if (data.TryGetProperty("private_key", out var key))
                secrets["certificate.key"] = key.GetString() ?? "";

            if (data.TryGetProperty("ca_chain", out var caChain))
                secrets["certificate.ca_chain"] = caChain.GetString() ?? "";
        }

        var leaseId = result.TryGetProperty("lease_id", out var leaseIdProp) ? leaseIdProp.GetString() : null;
        var leaseDuration = result.TryGetProperty("lease_duration", out var leaseDurationProp)
            ? TimeSpan.FromSeconds(leaseDurationProp.GetInt32())
            : (TimeSpan?)null;

        return new SecretResult
        {
            Secrets = secrets,
            LeaseId = leaseId,
            LeaseDuration = leaseDuration,
            Renewable = false,
            ExpireTime = leaseDuration.HasValue ? DateTimeOffset.UtcNow.Add(leaseDuration.Value) : null
        };
    }

    /// <inheritdoc />
    public bool CanHandle(string path)
    {
        return path.StartsWith(_options.BackendPath);
    }

    /// <inheritdoc />
    public TimeSpan? GetTtl(string path)
    {
        return _options.Ttl;
    }
}
