using System.Collections.Concurrent;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core.Exceptions;
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
/// <see cref="StartAsync"/> schedules periodic refresh cycles. Each due cycle
/// renews renewable leases before invoking <see cref="OnSecretsRefreshed"/>;
/// subscribers (typically the configuration provider) then re-load secret
/// material. A failed renewal marks its lease non-renewable for later cycles.
/// </para>
/// </remarks>
public class SecretRefresher : IDisposable, IHostedService
{
    private readonly VaultOptions _options;
    private readonly ILogger<SecretRefresher> _logger;
    private readonly ConcurrentDictionary<string, SecretMetadata> _secretMetadata = new();
    private readonly ISecretRefreshScheduler _scheduler;
    private readonly VaultLeaseRenewer _leaseRenewer;
    private int _isRefreshing;

    /// <summary>
    /// Raised at the end of each refresh cycle after the refresher determines
    /// that a refresh is due. Subscribers should reload their secret material
    /// and re- <see cref="TrackSecret"/> any new <see cref="SecretResult"/>s
    /// they obtain.
    /// </summary>
    public event Func<Task>? OnSecretsRefreshed;

    /// <summary>
    /// Initializes a secret refresher with the supplied scheduler and lease renewer.
    /// </summary>
    /// <param name="options">The Vault options controlling refresh behavior.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    /// <param name="scheduler">The scheduler that drives refresh cycles.</param>
    /// <param name="leaseRenewer">The component used to renew renewable Vault leases.</param>
    public SecretRefresher(
        VaultOptions options,
        ILogger<SecretRefresher> logger,
        ISecretRefreshScheduler scheduler,
        VaultLeaseRenewer leaseRenewer)
    {
        _options = options;
        _logger = logger;
        _scheduler = scheduler;
        _leaseRenewer = leaseRenewer;
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

        var interval = _options.Refresh.Interval;
        if (!interval.HasValue)
        {
            var minimumTtl = GetMinimumTtl();
            if (!minimumTtl.HasValue || minimumTtl.Value <= TimeSpan.Zero)
            {
                _logger.LogInformation("Secret refresh was not started because no lease TTL is tracked");
                return Task.CompletedTask;
            }

            interval = TimeSpan.FromTicks(minimumTtl.Value.Ticks * 8 / 10);
        }

        _scheduler.Start(interval.Value, RefreshLoopAsync);
        _logger.LogInformation("Secret refresh started with interval: {Interval}", interval.Value);
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
        _scheduler.Stop();
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

    private async Task RenewLeasesAsync(CancellationToken cancellationToken)
    {
        var renewableLeases = _secretMetadata
            .Where(entry => entry.Value.Renewable && entry.Value.LeaseId is not null)
            .ToArray();

        foreach (var (path, metadata) in renewableLeases)
        {
            try
            {
                var newDuration = await _leaseRenewer.RenewAsync(
                    metadata.LeaseId!,
                    metadata.LeaseDuration ?? TimeSpan.FromHours(1),
                    cancellationToken);

                if (newDuration.HasValue)
                {
                    var now = DateTimeOffset.UtcNow;
                    _secretMetadata.TryUpdate(
                        path,
                        metadata with
                        {
                            LeaseDuration = newDuration,
                            ExpireTime = now.Add(newDuration.Value),
                            LastRefreshed = now
                        },
                        metadata);
                }
            }
            catch (VaultLeaseRenewalException)
            {
                _secretMetadata.TryUpdate(
                    path,
                    metadata with { Renewable = false },
                    metadata);
            }
        }
    }

    private async Task RefreshLoopAsync()
    {
        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0)
        {
            _logger.LogWarning("Previous refresh still running, skipping");
            return;
        }

        try
        {
            if (!ShouldRefresh())
                return;

            await RenewLeasesAsync(CancellationToken.None);

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
            Interlocked.Exchange(ref _isRefreshing, 0);
        }
    }

    /// <summary>
    /// Releases the resources held by the refresher, including the
    /// background refresh timer.
    /// </summary>
    public void Dispose()
    {
        _scheduler.Dispose();
    }
}

/// <summary>
/// In-memory record of the lease metadata for a single tracked secret.
/// </summary>
internal sealed record SecretMetadata
{
    /// <summary>
    /// Gets or initializes the logical path of the tracked secret.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Vault lease identifier, when the secret is leased.
    /// </summary>
    public string? LeaseId { get; init; }

    /// <summary>
    /// Gets or initializes the lease duration returned by Vault, when known.
    /// </summary>
    public TimeSpan? LeaseDuration { get; init; }

    /// <summary>
    /// Gets or initializes the absolute time at which the secret expires, if known.
    /// </summary>
    public DateTimeOffset? ExpireTime { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the lease can be renewed.
    /// </summary>
    public bool Renewable { get; init; }

    /// <summary>
    /// Gets or initializes the UTC time at which the secret was last refreshed.
    /// </summary>
    public DateTimeOffset LastRefreshed { get; init; }
}
