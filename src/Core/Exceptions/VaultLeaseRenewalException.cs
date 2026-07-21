namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when the library fails to renew a dynamic secret or token lease with the Vault server.
/// </summary>
/// <remarks>
/// Exposes the <see cref="LeaseId"/> of the lease that could not be renewed so callers can
/// correlate the failure with Vault audit logs and decide whether to retry or revoke.
/// </remarks>
public class VaultLeaseRenewalException : VaultException
{
    /// <summary>
    /// The identifier of the lease that could not be renewed.
    /// </summary>
    public string LeaseId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultLeaseRenewalException"/> class
    /// for a failed renewal of the specified <paramref name="leaseId"/>.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that failed to renew.</param>
    /// <param name="message">A description of the renewal failure.</param>
    public VaultLeaseRenewalException(string leaseId, string message)
        : base($"Failed to renew lease '{leaseId}': {message}")
    {
        LeaseId = leaseId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultLeaseRenewalException"/> class
    /// for a failed renewal of the specified <paramref name="leaseId"/> with a reference
    /// to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that failed to renew.</param>
    /// <param name="message">A description of the renewal failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VaultLeaseRenewalException(string leaseId, string message, Exception innerException)
        : base($"Failed to renew lease '{leaseId}': {message}", innerException)
    {
        LeaseId = leaseId;
    }
}
