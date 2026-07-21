using System.Text;
using System.Text.Json;
using DotNet.Vault.Configuration.Core;

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
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PkiSecretBackend"/> class.
    /// </summary>
    /// <param name="options">The PKI backend options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault API.</param>
    public PkiSecretBackend(PkiSecretBackendOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string BackendType => "pki";

    /// <inheritdoc />
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            common_name = _options.CommonName,
            alt_names = string.Join(",", _options.AltNames),
            ttl = _options.Ttl?.TotalSeconds.ToString()
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"/v1/{request.Path}", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
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
