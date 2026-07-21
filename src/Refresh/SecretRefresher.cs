namespace DotNet.Vault.Configuration.Refresh;

/// <summary>
/// Placeholder stub used by <see cref="DotNet.Vault.Configuration.Core.VaultConfigurationProvider"/>
/// until Task 10 delivers the full implementation.
/// </summary>
/// <remarks>
/// This stub intentionally reports no minimum lease TTL and never asks for a
/// refresh, so the provider behaves like a one-shot loader. Task 10 will
/// replace the body with the real lease-tracking implementation; the
/// public surface (<see cref="GetMinimumTtl"/> and <see cref="ShouldRefresh"/>)
/// is what the provider already consumes.
/// </remarks>
public class SecretRefresher
{
    /// <summary>
    /// Returns the shortest remaining TTL across the cached secrets.
    /// </summary>
    /// <returns>The minimum TTL, or <see langword="null"/> when nothing is tracked.</returns>
    public TimeSpan? GetMinimumTtl()
    {
        return null;
    }

    /// <summary>
    /// Indicates whether the provider should trigger a refresh on the next
    /// timer tick.
    /// </summary>
    /// <returns><see langword="true"/> when a refresh is due; otherwise <see langword="false"/>.</returns>
    public bool ShouldRefresh()
    {
        return false;
    }
}
