using DotNet.Vault.Configuration.Authentication;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Http;

/// <summary>
/// DelegatingHandler that automatically attaches the X-Vault-Token header
/// to every outgoing HTTP request by resolving the configured auth provider.
/// </summary>
public class VaultAuthDelegatingHandler : DelegatingHandler
{
    private readonly IVaultAuthenticationProvider _authProvider;
    private readonly ILogger<VaultAuthDelegatingHandler> _logger;

    public VaultAuthDelegatingHandler(
        IVaultAuthenticationProvider authProvider,
        ILogger<VaultAuthDelegatingHandler> logger)
    {
        _authProvider = authProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains("X-Vault-Token"))
        {
            try
            {
                var token = await _authProvider.GetTokenAsync(cancellationToken);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Add("X-Vault-Token", token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to attach X-Vault-Token; request may fail");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
