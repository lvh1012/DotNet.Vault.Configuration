using System.Text.Json;
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;

namespace DotNet.Vault.Configuration.Backends;

/// <summary>
/// Vault database secret backend that issues short-lived dynamic
/// database credentials through the configured Vault role.
/// </summary>
/// <remarks>
/// <see cref="GetSecretsAsync"/> issues a <c>GET</c> against
/// <c>/v1/{path}</c> and flattens the <c>data</c> envelope into a
/// <see cref="SecretResult"/>. An optional <see cref="DatabaseSecretBackendOptions.PropertyPrefix"/>
/// is applied to every emitted key.
/// </remarks>
public class DatabaseSecretBackend : IVaultSecretBackend
{
    private readonly DatabaseSecretBackendOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IVaultAuthenticationProvider? _authProvider;

    /// Initializes a new instance of the <see cref="DatabaseSecretBackend"/> class.
    /// </summary>
    /// <param name="options">The database backend options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault API.</param>
    /// <param name="authProvider">The optional authentication provider used to attach an <c>X-Vault-Token</c> header to outgoing requests.</param>
    public DatabaseSecretBackend(DatabaseSecretBackendOptions options, HttpClient httpClient, IVaultAuthenticationProvider? authProvider = null)
    {
        _options = options;
        _httpClient = httpClient;
        _authProvider = authProvider;
    }

    /// <inheritdoc />
    public string BackendType => "database";

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

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        var secrets = new Dictionary<string, string>();

        if (result.TryGetProperty("data", out var data))
        {
            foreach (var prop in data.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(_options.PropertyPrefix)
                    ? prop.Name
                    : $"{_options.PropertyPrefix}.{prop.Name}";
                secrets[key] = prop.Value.ToString();
            }
        }

        var leaseId = result.TryGetProperty("lease_id", out var leaseIdProp) ? leaseIdProp.GetString() : null;
        var leaseDuration = result.TryGetProperty("lease_duration", out var leaseDurationProp)
            ? TimeSpan.FromSeconds(leaseDurationProp.GetInt32())
            : (TimeSpan?)null;
        var renewable = result.TryGetProperty("renewable", out var renewableProp) && renewableProp.GetBoolean();

        return new SecretResult
        {
            Secrets = secrets,
            LeaseId = leaseId,
            LeaseDuration = leaseDuration,
            Renewable = renewable,
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
        return null;
    }
}
