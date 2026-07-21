using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Stub authentication provider for the Vault <c>cert</c> auth method.
/// </summary>
/// <remarks>
/// TLS certificate authentication requires loading a client certificate and
/// private key into the underlying <see cref="HttpClient"/> handler and is
/// not implemented in the initial version of this library. Selecting this
/// provider at runtime will cause <see cref="GetTokenAsync"/> to throw
/// <see cref="NotImplementedException"/>.
/// </remarks>
public class TlsCertificateAuthProvider : IVaultAuthenticationProvider
{
    private readonly TlsCertificateAuthenticationOptions _options;

    /// <summary>
    /// Creates a new <see cref="TlsCertificateAuthProvider"/> bound to the
    /// supplied options.
    /// </summary>
    /// <param name="options">The TLS certificate authentication options.</param>
    public TlsCertificateAuthProvider(IOptions<TlsCertificateAuthenticationOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string AuthenticationMethod => "cert";

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">
    /// Always thrown. TLS certificate authentication is not yet implemented.
    /// </exception>
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // TLS certificate authentication requires client cert handling - not implemented in this initial version
        throw new NotImplementedException("TLS certificate authentication is not yet implemented");
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
