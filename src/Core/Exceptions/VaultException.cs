namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Base exception type for all errors thrown by the DotNet.Vault.Configuration library.
/// </summary>
/// <remarks>
/// Catch this exception to handle any library-specific error generically. Derived
/// exception types expose additional context (status codes, lease ids, paths, etc.)
/// for more targeted handling at call sites.
/// </remarks>
public class VaultException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VaultException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public VaultException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}
