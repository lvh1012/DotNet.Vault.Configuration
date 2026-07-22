using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace DotNet.Vault.Configuration.Security;

/// <summary>
/// Configures SSL/TLS behavior for connections to Vault.
/// </summary>
/// <remarks>
/// Certificate paths are loaded by the HTTP client configuration. Certificate
/// instances can be supplied directly when they are managed outside the
/// filesystem, such as by a certificate store or secret provider.
/// </remarks>
public class VaultSslOptions
{
    /// <summary>
    /// Gets or sets the filesystem path to the CA certificate used to validate
    /// the Vault server certificate.
    /// </summary>
    /// <value>The CA certificate path, or <see langword="null"/> to use the system trust store.</value>
    public string? CaCertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the filesystem path to the client certificate used for mutual TLS.
    /// </summary>
    /// <value>The client certificate path, or <see langword="null"/> when no client certificate is configured.</value>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the password for the client certificate file.
    /// </summary>
    /// <value>The client certificate password, or <see langword="null"/> when it is not required.</value>
    public string? ClientCertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets the CA certificate used to validate the Vault server certificate.
    /// </summary>
    /// <value>The CA certificate, or <see langword="null"/> to use the system trust store.</value>
    public X509Certificate2? CaCertificate { get; set; }

    /// <summary>
    /// Gets or sets the client certificate used for mutual TLS.
    /// </summary>
    /// <value>The client certificate, or <see langword="null"/> when no client certificate is configured.</value>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether server certificate validation is skipped.
    /// </summary>
    /// <value><see langword="true"/> to skip validation; otherwise <see langword="false"/>.</value>
    public bool SkipVerify { get; set; } = false;

    /// <summary>
    /// Gets or sets the TLS protocol used for Vault connections.
    /// </summary>
    /// <value>The TLS protocol. Defaults to <see cref="SslProtocols.Tls12"/>.</value>
    public SslProtocols Protocol { get; set; } = SslProtocols.Tls12;

    /// <summary>
    /// Gets or sets a value indicating whether certificate revocation is checked.
    /// </summary>
    /// <value><see langword="true"/> to check revocation; otherwise <see langword="false"/>.</value>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>
    /// Gets or sets the server name used for TLS Server Name Indication validation.
    /// </summary>
    /// <value>The server name, or <see langword="null"/> to use the Vault URI host.</value>
    public string? ServerName { get; set; }
}
