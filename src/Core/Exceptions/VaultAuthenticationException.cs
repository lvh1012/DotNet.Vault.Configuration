namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when the library fails to authenticate with the Vault server using the configured method.
/// </summary>
/// <remarks>
/// Carries the <see cref="AuthenticationMethod"/> (for example <c>token</c>, <c>userpass</c>,
/// <c>approle</c>, <c>kubernetes</c>) so callers can branch on the auth approach when reporting or
/// recovering from the failure.
/// </remarks>
public class VaultAuthenticationException : VaultException
{
    /// <summary>
    /// The authentication method that failed (e.g. <c>token</c>, <c>userpass</c>, <c>approle</c>).
    /// </summary>
    public string AuthenticationMethod { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultAuthenticationException"/> class
    /// for a failed authentication using <paramref name="method"/>.
    /// </summary>
    /// <param name="method">The authentication method that failed.</param>
    /// <param name="message">A description of the authentication failure.</param>
    public VaultAuthenticationException(string method, string message)
        : base($"Authentication failed for method '{method}': {message}")
    {
        AuthenticationMethod = method;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultAuthenticationException"/> class
    /// for a failed authentication using <paramref name="method"/>, with a reference to the
    /// inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="method">The authentication method that failed.</param>
    /// <param name="message">A description of the authentication failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VaultAuthenticationException(string method, string message, Exception innerException)
        : base($"Authentication failed for method '{method}': {message}", innerException)
    {
        AuthenticationMethod = method;
    }
}
