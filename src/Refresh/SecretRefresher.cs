using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Refresh;

/// <summary>
/// Tracks the lease metadata of secrets resolved by the configured Vault
/// backends and drives a background refresh cycle that re-loads secrets
/// before any tracked lease expires.
/// </summary>
/// <remarks>
/// <para>
/// The refresher is registered as a singleton and is consumed by
/// <see cref="Core.VaultConfigurationProvider"/> to decide when to reload.
/// Callers should <see cref="TrackSecret"/> every <see cref="SecretResult"/>
/// they obtain so the refresher can compute the minimum TTL and decide
/// whether a refresh is due via <see cref="GetMinimumTtl"/> and
/// <see cref="ShouldRefresh"/>.
/// </para>
/// <para>
/// When <see cref="VaultRefreshOptions.Enabled"/> is <see langword="true"/>,
/// <see cref="StartAsync"/> schedules a periodic timer that invokes the
/// <see cref="OnSecretsRefreshed"/> event handler; subscribers (typically
/// the configuration provider) perform the actual re-load of secret material.
/// </para>
/// </remarks>
public class SecretRefresher : IDisposable, IHostedService
{
    private readonly Lazy<VaultClient> _client;
    private readonly VaultOptions _options;
    private readonly ILogger<SecretRefresher> _logger;
    private readonly Dictionary<string, SecretMetadata> _secretMetadata = new();
    private Timer? _refreshTimer;
    private bool _isRefreshing;

    /// <summary>
    /// Raised at the end of each refresh cycle after the refresher determines
    /// that a refresh is due. Subscribers should reload their secret material
    /// and re- <see cref="TrackSecret"/> any new <see cref="SecretResult"/>s
    /// they obtain.
    /// </summary>
    public event Func<Task>? OnSecretsRefreshed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretRefresher"/> class.
    /// </summary>
    /// <param name="client">The lazy Vault client used to interact with the Vault server.</param>
    /// <param name="options">The configured <see cref="VaultOptions"/>.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    public SecretRefresher(
        Lazy<VaultClient> client,
        VaultOptions options,
        ILogger<SecretRefresher> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Starts the background refresh timer when
    /// <see cref="VaultRefreshOptions.Enabled"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel startup.</param>
    /// <returns>A task that completes when the timer has been scheduled.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Refresh.Enabled)
        {
            _logger.LogInformation("Secret refresh is disabled");
            return Task.CompletedTask;
        }

        var interval = _options.Refresh.Interval ?? TimeSpan.FromMinutes(5);

        _refreshTimer = new Timer(
            async _ => await RefreshLoopAsync(),
            null,
            interval,
            interval);

        _logger.LogInformation("Secret refresh started with interval: {Interval}", interval);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background refresh timer, preventing further refresh cycles
    /// from firing.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel shutdown.</param>
    /// <returns>A task that completes when the timer has been disabled.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _refreshTimer?.Change(Timeout.Infinite, 0);
        _logger.LogInformation("Secret refresh stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records the lease metadata of a resolved secret so the refresher can
    /// compute remaining TTLs and decide when a refresh is due.
    /// </summary>
    /// <remarks>
    /// Secrets without a lease (<c>LeaseDuration</c> and <c>ExpireTime</c>
    /// both <see langword="null"/>) are ignored, since there is no TTL to
    /// track and no automatic refresh to schedule.
    /// </remarks>
    /// <param name="path">The logical path of the secret.</param>
    /// <param name="result">The <see cref="SecretResult"/> returned by the backend.</param>
    public void TrackSecret(string path, SecretResult result)
    {
        if (result.LeaseDuration.HasValue || result.ExpireTime.HasValue)
        {
            _secretMetadata[path] = new SecretMetadata
            {
                Path = path,
                LeaseId = result.LeaseId,
                LeaseDuration = result.LeaseDuration,
                ExpireTime = result.ExpireTime,
                Renewable = result.Renewable,
                LastRefreshed = DateTimeOffset.UtcNow
            };

            _logger.LogDebug("Tracked secret at {Path} with TTL {Ttl}", path, result.LeaseDuration);
        }
    }

    /// <summary>
    /// Returns the shortest remaining TTL across the cached secrets.
    /// </summary>
    /// <returns>The minimum TTL, or <see langword="null"/> when nothing is tracked.</returns>
    public TimeSpan? GetMinimumTtl()
    {
        if (!_secretMetadata.Any())
            return null;

        return _secretMetadata.Values
            .Where(m => m.LeaseDuration.HasValue)
            .Min(m => m.LeaseDuration);
    }

    /// <summary>
    /// Indicates whether the provider should trigger a refresh on the next
    /// timer tick.
    /// </summary>
    /// <remarks>
    /// A refresh is due when any tracked secret has less than 20% of its
    /// lease duration remaining before its expected expiry.
    /// </remarks>
    /// <returns><see langword="true"/> when a refresh is due; otherwise <see langword="false"/>.</returns>
    public bool ShouldRefresh()
    {
        if (!_secretMetadata.Any())
            return false;

        var now = DateTimeOffset.UtcNow;

        return _secretMetadata.Values.Any(m =>
        {
            if (!m.LeaseDuration.HasValue)
                return false;

            var timeUntilExpiry = m.ExpireTime ?? m.LastRefreshed.Add(m.LeaseDuration.Value);
            var timeRemaining = timeUntilExpiry - now;
            var threshold = m.LeaseDuration.Value * 0.2;

            return timeRemaining < threshold;
        });
    }

    private async Task RefreshLoopAsync()
    {
        if (_isRefreshing)
        {
            _logger.LogWarning("Previous refresh still running, skipping");
            return;
        }

        try
        {
            _isRefreshing = true;

            if (!ShouldRefresh())
                return;

            _logger.LogInformation("Starting secret refresh cycle");

            if (OnSecretsRefreshed != null)
            {
                await OnSecretsRefreshed.Invoke();
            }

            _logger.LogInformation("Secret refresh cycle completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secret refresh cycle");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Releases the resources held by the refresher, including the
    /// background refresh timer.
    /// </summary>
    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}

/// <summary>
/// In-memory record of the lease metadata for a single tracked secret.
/// </summary>
internal class SecretMetadata
{
    /// <summary>
    /// Gets or sets the logical path of the tracked secret.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Vault lease identifier, when the secret is leased.
    /// </summary>
    public string? LeaseId { get; set; }

    /// <summary>
    /// Gets or sets the lease duration returned by Vault, when known.
    /// </summary>
    public TimeSpan? LeaseDuration { get; set; }

    /// <summary>
    /// Gets or sets the absolute time at which the secret expires, if known.
    /// </summary>
    public DateTimeOffset? ExpireTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the lease can be renewed.
    /// </summary>
    public bool Renewable { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the secret was last refreshed.
    /// </summary>
    public DateTimeOffset LastRefreshed { get; set; }
}
