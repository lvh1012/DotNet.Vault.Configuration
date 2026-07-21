using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.HealthChecks;

public static class HealthChecksBuilderExtensions
{
    public static IHealthChecksBuilder AddVault(
        this IHealthChecksBuilder builder,
        string name = "vault",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        builder.Add(new HealthCheckRegistration(
            name,
            sp => new VaultHealthCheck(
                sp.GetRequiredService<VaultClient>(),
                sp.GetRequiredService<SecretRefresher>(),
                sp.GetRequiredService<VaultOptions>(),
                sp.GetRequiredService<ILogger<VaultHealthCheck>>()),
            failureStatus,
            tags,
            timeout));

        return builder;
    }
}
