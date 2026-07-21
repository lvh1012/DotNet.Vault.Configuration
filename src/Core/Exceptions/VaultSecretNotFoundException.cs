namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when a requested secret does not exist at the specified Vault path.
/// </summary>
/// <remarks>
/// The <see cref="Path"/> property carries the secret path that was looked up, which is
/// useful for diagnostics and for deciding between misconfiguration and missing data
/// at call sites.
/// </remarks>
public class VaultSecretNotFoundException : VaultException
{
    /// <summary>
    /// The Vault path at which the secret was expected to exist.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultSecretNotFoundException"/> class
    /// for a missing secret at the specified <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The Vault path that was queried.</param>
    public VaultSecretNotFoundException(string path)
        : base($"Secret not found at path: {path}")
    {
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultSecretNotFoundException"/> class
    /// for a missing secret at the specified <paramref name="path"/>, with an additional
    /// diagnostic message.
    /// </summary>
    /// <param name="path">The Vault path that was queried.</param>
    /// <param name="message">A descriptive message explaining the failure.</param>
    public VaultSecretNotFoundException(string path, string message)
        : base($"Secret not found at path '{path}': {message}")
    {
        Path = path;
    }
}
