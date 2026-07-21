using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Core;

/// <summary>
/// <see cref="IConfigurationProvider"/> implementation that materializes the
/// secrets loaded from a Vault server into the <c>Data</c> dictionary that
/// drives <see cref="IConfigurationRoot"/>.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="Load"/> the provider resolves the set of logical secret paths
/// from <see cref="VaultOptions"/> (KV, database, PKI) and asks the injected
/// <see cref="VaultClient"/> to fetch them. The aggregated key/value pairs are
/// then exposed through the inherited <c>Data</c> bag.
/// </para>
/// <para>
/// When <see cref="VaultRefreshOptions.Enabled"/> is <see langword="true"/> and
/// the <see cref="SecretRefresher"/> reports a positive minimum TTL, a
/// background <see cref="Timer"/> is scheduled that periodically asks the
/// refresher whether a refresh is due and, if so, reloads the secrets and
/// raises <see cref="ConfigurationProvider.OnReload"/>.
/// </para>
/// <para>
/// Failures during the initial load are surfaced through
/// <see cref="VaultOptions.FailFast"/>: when the option is enabled the
/// exception is rethrown; otherwise an empty configuration is used and a
/// warning is logged.
/// </para>
/// </remarks>
public class VaultConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly VaultClient _client;
    private readonly VaultOptions _options;
    private readonly SecretRefresher _refresher;
    private readonly ILogger<VaultConfigurationProvider> _logger;
    private Timer? _refreshTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultConfigurationProvider"/> class.
    /// </summary>
    /// <param name="client">The <see cref="VaultClient"/> used to load secrets from Vault.</param>
    /// <param name="options">The configured <see cref="VaultOptions"/>.</param>
    /// <param name="refresher">The <see cref="SecretRefresher"/> consulted to drive background refreshes.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public VaultConfigurationProvider(
        VaultClient client,
        VaultOptions options,
        SecretRefresher refresher,
        ILogger<VaultConfigurationProvider> logger)
    {
        _client = client;
        _options = options;
        _refresher = refresher;
        _logger = logger;
    }

    /// <summary>
    /// Loads the configured secrets from Vault synchronously by blocking on the
    /// asynchronous load pipeline.
    /// </summary>
    /// <remarks>
    /// This override satisfies the <see cref="ConfigurationProvider"/> contract,
    /// which is synchronous; the asynchronous work is performed by
    /// <c>LoadAsync</c> and awaited through <see cref="Task.GetAwaiter"/>.
    /// </remarks>
    public override void Load()
    {
        LoadAsync().GetAwaiter().GetResult();
    }

    private async Task LoadAsync()
    {
        try
        {
            var paths = BuildSecretPaths();
            var secrets = await _client.LoadSecretsAsync(paths);
            Data = secrets;
            SetupRefreshIfNeeded();
            _logger.LogInformation("Loaded {Count} secrets from Vault", secrets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load secrets from Vault");
            if (_options.FailFast)
                throw;

            _logger.LogWarning("FailFast is disabled. Continuing with empty configuration.");
            Data = new Dictionary<string, string>();
        }
    }

    private List<string> BuildSecretPaths()
    {
        var paths = new List<string>();

        if (_options.Kv.Enabled)
        {
            var kvPaths = KvPathBuilder.BuildPaths(_options.Kv);
            paths.AddRange(kvPaths);
        }

        if (_options.Database.Enabled)
        {
            paths.Add($"{_options.Database.BackendPath}/creds/{_options.Database.Role}");
        }

        if (_options.Pki.Enabled)
        {
            paths.Add($"{_options.Pki.BackendPath}/issue/{_options.Pki.Role}");
        }

        return paths;
    }

    private void SetupRefreshIfNeeded()
    {
        if (!_options.Refresh.Enabled)
            return;

        var ttl = _refresher.GetMinimumTtl();
        if (ttl.HasValue && ttl.Value > TimeSpan.Zero)
        {
            var refreshInterval = _options.Refresh.Interval ?? TimeSpan.FromTicks(ttl.Value.Ticks * 8 / 10);

            _refreshTimer = new Timer(
                async _ => await RefreshAsync(),
                null,
                refreshInterval,
                refreshInterval);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (_refresher.ShouldRefresh())
            {
                _logger.LogInformation("Refreshing secrets from Vault");
                var paths = BuildSecretPaths();
                var secrets = await _client.LoadSecretsAsync(paths);
                Data = secrets;
                OnReload();
                _logger.LogInformation("Refreshed {Count} secrets", secrets.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh secrets from Vault");
        }
    }

    /// <summary>
    /// Disposes the underlying refresh <see cref="Timer"/>, if one was created.
    /// </summary>
    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
