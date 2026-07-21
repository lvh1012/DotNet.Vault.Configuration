namespace DotNet.Vault.Configuration.Backends;

/// <summary>
/// Abstraction for Vault secret backends (key/value, database, PKI, ...)
/// that resolve logical secret paths into key/value material retrieved from
/// a configured Vault server.
/// </summary>
/// <remarks>
/// Implementations are responsible for translating a logical <see cref="SecretRequest"/>
/// into the backend-specific Vault API call and producing a normalized
/// <see cref="SecretResult"/> that the configuration provider can project onto
/// <c>IConfiguration</c>.
/// </remarks>
public interface IVaultSecretBackend
{
    /// <summary>
    /// Gets a short identifier for the backend type (for example <c>kv</c>,
    /// <c>database</c>, or <c>pki</c>).
    /// </summary>
    string BackendType { get; }

    /// <summary>
    /// Fetches the secret material exposed at the logical <paramref name="request"/> path.
    /// </summary>
    /// <param name="request">The logical secret path and any backend-specific parameters.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The resolved secret values and lease metadata.</returns>
    Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the backend can resolve the given logical path.
    /// </summary>
    /// <param name="path">The logical secret path.</param>
    /// <returns><see langword="true"/> when the backend owns the path; otherwise <see langword="false"/>.</returns>
    bool CanHandle(string path);

    /// <summary>
    /// Returns the lease TTL, if any, associated with the value at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The logical secret path.</param>
    /// <returns>The remaining lease time, or <see langword="null"/> when the secret is not leased.</returns>
    TimeSpan? GetTtl(string path);
}

/// <summary>
/// Describes a logical secret lookup that is dispatched to a
/// <see cref="IVaultSecretBackend"/> implementation.
/// </summary>
public class SecretRequest
{
    /// <summary>
    /// Gets or sets the logical secret path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional backend-specific parameters.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Represents the resolved material returned from a
/// <see cref="IVaultSecretBackend.GetSecretsAsync"/> call.
/// </summary>
public class SecretResult
{
    /// <summary>
    /// Gets or sets the resolved secret values, keyed by their Vault property name.
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = new();

    /// <summary>
    /// Gets or sets the Vault lease identifier, when the secret is leased.
    /// </summary>
    public string? LeaseId { get; set; }

    /// <summary>
    /// Gets or sets the lease duration, when the secret is leased.
    /// </summary>
    public TimeSpan? LeaseDuration { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the lease can be renewed.
    /// </summary>
    public bool Renewable { get; set; }

    /// <summary>
    /// Gets or sets the absolute time at which the secret expires, if known.
    /// </summary>
    public DateTimeOffset? ExpireTime { get; set; }
}
