namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when a configured secret backend is not supported or not enabled on the target Vault server.
/// </summary>
/// <remarks>
/// The <see cref="BackendType"/> property (for example <c>kv</c>, <c>database</c>, <c>aws</c>) identifies
/// which backend was rejected so operators can verify server capabilities or configuration.
/// </remarks>
public class VaultBackendNotSupportedException : VaultException
{
    /// <summary>
    /// The type identifier of the unsupported or disabled backend (e.g. <c>kv</c>, <c>database</c>).
    /// </summary>
    public string BackendType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultBackendNotSupportedException"/> class
    /// for a backend that is not supported or not enabled on the target Vault server.
    /// </summary>
    /// <param name="backendType">The type identifier of the rejected backend.</param>
    public VaultBackendNotSupportedException(string backendType)
        : base($"Secret backend '{backendType}' is not supported or not enabled")
    {
        BackendType = backendType;
    }
}
