using System.Text.Json;
using DotNet.Vault.Configuration.Core;

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
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="KvSecretBackend"/> class.
    /// </summary>
    /// <param name="options">The KV backend options.</param>
    /// <param name="httpClient">The HTTP client used to call the Vault API.</param>
    public KvSecretBackend(KvSecretBackendOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string BackendType => "kv";

    /// <inheritdoc />
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/v1/{request.Path}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
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

        return new SecretResult
        {
            Secrets = secrets,
            LeaseDuration = null,
            Renewable = false
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
