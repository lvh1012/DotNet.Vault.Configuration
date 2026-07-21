namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when the library cannot establish a connection to the Vault server.
/// </summary>
/// <remarks>
/// Typically wraps lower-level network or socket exceptions and exposes the
/// targeted <see cref="VaultUri"/> for diagnostics and logging.
/// </remarks>
public class VaultConnectionException : VaultException
{
    /// <summary>
    /// The Vault server URI the library attempted to connect to.
    /// </summary>
    public Uri VaultUri { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultConnectionException"/> class
    /// for a failed connection attempt to <paramref name="vaultUri"/>.
    /// </summary>
    /// <param name="vaultUri">The Vault server URI that could not be reached.</param>
    /// <param name="innerException">The underlying network or transport exception.</param>
    public VaultConnectionException(Uri vaultUri, Exception innerException)
        : base($"Failed to connect to Vault at {vaultUri}", innerException)
    {
        VaultUri = vaultUri;
    }
}
