using DotNet.Vault.Configuration.Core.Exceptions;
using DotNet.Vault.Configuration.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Refresh;

/// <summary>
/// Renews Vault leases via /v1/sys/leases/renew.
/// </summary>
public class VaultLeaseRenewer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VaultLeaseRenewer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultLeaseRenewer"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The factory used to create the authenticated Vault HTTP client.</param>
    /// <param name="logger">The logger for renewal operations.</param>
    public VaultLeaseRenewer(
        IHttpClientFactory httpClientFactory,
        ILogger<VaultLeaseRenewer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Renews the specified Vault lease.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease to renew.</param>
    /// <param name="increment">The requested lease extension.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The new lease duration returned by Vault, or <see langword="null"/> when the
    /// response does not include a <c>lease_duration</c> field.
    /// </returns>
    /// <exception cref="VaultLeaseRenewalException">Thrown when the renewal request fails.</exception>
    public async Task<TimeSpan?> RenewAsync(
        string leaseId,
        TimeSpan increment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
            var payload = new { lease_id = leaseId, increment = (int)increment.TotalSeconds };
            var response = await client.PutAsJsonAsync("/v1/sys/leases/renew", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return result.TryGetProperty("lease_duration", out var d) ? TimeSpan.FromSeconds(d.GetInt32()) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to renew lease {LeaseId}", leaseId);
            throw new VaultLeaseRenewalException(leaseId, ex.Message);
        }
    }
}
