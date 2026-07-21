namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Thrown when the Vault HTTP API returns a non-success response or an otherwise unparseable error.
/// </summary>
/// <remarks>
/// Exposes the HTTP <see cref="StatusCode"/>, the Vault <see cref="ErrorCode"/> (when provided),
/// and the <see cref="RequestId"/> for correlation with Vault audit logs.
/// </remarks>
public class VaultApiException : VaultException
{
    /// <summary>
    /// The HTTP status code returned by the Vault API.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The Vault error code, if one was returned in the response body.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// The Vault request identifier, when the response included one for log correlation.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultApiException"/> class
    /// for a failed Vault API call.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by Vault.</param>
    /// <param name="message">The human-readable error message from Vault.</param>
    /// <param name="errorCode">The Vault error code, if available.</param>
    /// <param name="requestId">The Vault request identifier, if available.</param>
    public VaultApiException(int statusCode, string message, string? errorCode = null, string? requestId = null)
        : base($"Vault API error (HTTP {statusCode}): {message}")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }
}
