using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.HealthChecks;

/// <summary>
/// <see cref="IHealthCheck"/> implementation that reports the runtime health of
/// the configured Vault server, the validity of its authentication, and the
/// freshness of any tracked secrets.
/// </summary>
/// <remarks>
/// <para>
/// The check is intended to be registered with
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> and is composed of three
/// signals:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>the <c>/v1/sys/health</c> payload reported by
/// <see cref="VaultClient.GetHealthAsync"/>;</description>
/// </item>
/// <item>
/// <description>the result of
/// <see cref="VaultClient.IsAuthenticationValidAsync"/>;</description>
/// </item>
/// <item>
/// <description>the minimum remaining lease TTL reported by
/// <see cref="SecretRefresher.GetMinimumTtl"/>.</description>
/// </item>
/// </list>
/// <para>
/// A sealed or uninitialized server produces <see cref="HealthStatus.Unhealthy"/>;
/// invalid authentication or a TTL under five minutes produces
/// <see cref="HealthStatus.Degraded"/>; otherwise the result is
/// <see cref="HealthStatus.Healthy"/>. Diagnostic data is always attached to the
/// returned <see cref="HealthCheckResult"/>.
/// </para>
/// </remarks>
public class VaultHealthCheck : IHealthCheck
{
    private readonly VaultClient _client;
    private readonly SecretRefresher _refresher;
    private readonly VaultOptions _options;
    private readonly ILogger<VaultHealthCheck> _logger;

    public VaultHealthCheck(
        VaultClient client,
        SecretRefresher refresher,
        VaultOptions options,
        ILogger<VaultHealthCheck> logger)
    {
        _client = client;
        _refresher = refresher;
        _options = options;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var vaultHealth = await _client.GetHealthAsync(cancellationToken);

            if (!vaultHealth.Initialized)
                return HealthCheckResult.Unhealthy("Vault is not initialized");

            if (vaultHealth.Sealed)
                return HealthCheckResult.Unhealthy("Vault is sealed");

            var isAuthValid = await _client.IsAuthenticationValidAsync(cancellationToken);
            if (!isAuthValid)
                return HealthCheckResult.Degraded("Vault authentication is invalid or expired");

            var minTtl = _refresher.GetMinimumTtl();
            var data = new Dictionary<string, object>
            {
                ["vault_version"] = vaultHealth.Version,
                ["vault_cluster"] = vaultHealth.ClusterName,
                ["vault_server_time"] = vaultHealth.ServerTimeUtc
            };

            if (minTtl.HasValue)
            {
                data["minimum_secret_ttl"] = minTtl.Value.ToString();

                if (minTtl.Value < TimeSpan.FromMinutes(5))
                    return HealthCheckResult.Degraded("Some secrets are expiring soon", data: data);
            }

            return HealthCheckResult.Healthy("Vault is healthy", data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vault health check failed");
            return HealthCheckResult.Unhealthy("Failed to connect to Vault", ex);
        }
    }
}
