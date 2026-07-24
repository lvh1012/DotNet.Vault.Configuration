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
    private readonly Func<CancellationToken, Task<string>>? _tokenProvider;


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

    internal VaultLeaseRenewer(
        Core.VaultClient client,
        ILogger<VaultLeaseRenewer> logger)
        : this(client.HttpClientFactory, logger)
    {
        _tokenProvider = client.GetTokenAsync;
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
        if (string.IsNullOrWhiteSpace(leaseId))
        {
            throw new ArgumentException("Lease ID cannot be null or empty.", nameof(leaseId));
        }

        if (increment < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Increment cannot be negative.");
        }

        if (increment.TotalSeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Increment exceeds the maximum supported value.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
            var payload = new { lease_id = leaseId, increment = (int)increment.TotalSeconds };
            using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/sys/leases/renew")
            {
                Content = JsonContent.Create(payload)
            };
            if (_tokenProvider is not null)
            {
                var token = await _tokenProvider(cancellationToken);
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Add("X-Vault-Token", token);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return result.TryGetProperty("lease_duration", out var d) ? TimeSpan.FromSeconds(d.GetInt32()) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to renew lease {LeaseId}", leaseId);
            throw new VaultLeaseRenewalException(leaseId, ex.Message, ex);
        }
    }
}
