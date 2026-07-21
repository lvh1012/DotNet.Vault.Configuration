namespace DotNet.Vault.Configuration.Authentication;

/// <summary>
/// Abstraction for Vault authentication providers that obtain and manage a
/// client token for the configured Vault server.
/// </summary>
/// <remarks>
/// Implementations are responsible for performing the underlying Vault auth
/// flow (static token, AppRole, Kubernetes, AWS, LDAP, certificate, ...)
/// and exposing the resulting token to consumers of this library.
/// </remarks>
public interface IVaultAuthenticationProvider
{
    /// <summary>
    /// Gets the Vault authentication method identifier (for example
    /// <c>token</c>, <c>approle</c>, or <c>kubernetes</c>).
    /// </summary>
    string AuthenticationMethod { get; }

    /// <summary>
    /// Returns a valid Vault client token, authenticating with the configured
    /// method when necessary.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The Vault client token.</returns>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a refresh of the cached token, re-authenticating with the
    /// configured Vault method.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the provider currently holds a valid token.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when a token is available; otherwise <see langword="false"/>.</returns>
    Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default);
}
