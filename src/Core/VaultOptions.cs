using DotNet.Vault.Configuration.Security;

namespace DotNet.Vault.Configuration.Core;

/// <summary>
/// Root configuration options for the DotNet.Vault.Configuration library.
/// </summary>
/// <remarks>
/// Bind a section of <c>IConfiguration</c> to this type (typically under the
/// <c>Vault</c> section) to configure the Vault server endpoint, the selected
/// authentication method, the enabled secret backends, and the refresh/retry
/// behavior used by the library.
/// </remarks>
public class VaultOptions
{
    /// <summary>
    /// Gets or sets the base URI of the Vault server.
    /// </summary>
    /// <value>The Vault server URI. Defaults to <c>http://localhost:8200</c>.</value>
    public Uri Uri { get; set; } = new Uri("http://localhost:8200");

    /// <summary>
    /// Gets or sets the optional Vault Enterprise namespace used to scope requests.
    /// </summary>
    /// <value>The namespace identifier, or <see langword="null"/> when not using namespaces.</value>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the SSL/TLS options used for connections to Vault.
    /// </summary>
    public VaultSslOptions Ssl { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-request timeout applied to HTTP calls against Vault.
    /// </summary>
    /// <value>The request timeout. Defaults to 30 seconds.</value>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the authentication configuration used to obtain a Vault token.
    /// </summary>
    public VaultAuthenticationConfiguration Authentication { get; set; } = new();

    /// <summary>
    /// Gets or sets the Key/Value (KV) secret backend options.
    /// </summary>
    public KvSecretBackendOptions Kv { get; set; } = new();

    /// <summary>
    /// Gets or sets the database secret backend options.
    /// </summary>
    public DatabaseSecretBackendOptions Database { get; set; } = new();

    /// <summary>
    /// Gets or sets the PKI secret backend options.
    /// </summary>
    public PkiSecretBackendOptions Pki { get; set; } = new();

    /// <summary>
    /// Gets or sets the refresh options that govern background renewal of secrets.
    /// </summary>
    public VaultRefreshOptions Refresh { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the library should fail fast during
    /// startup when Vault is unreachable or misconfigured.
    /// </summary>
    /// <value><see langword="true"/> to throw on startup errors; otherwise <see langword="false"/>.</value>
    public bool FailFast { get; set; } = true;
}

/// <summary>
/// Configures the Vault authentication method and the per-method settings used
/// to obtain a client token.
/// </summary>
/// <remarks>
/// Set <see cref="Method"/> to one of the supported auth method names
/// (for example <c>token</c>, <c>approle</c>, <c>kubernetes</c>, <c>aws</c>,
/// <c>ldap</c>, or <c>cert</c>) and populate the matching options property.
/// </remarks>
public class VaultAuthenticationConfiguration
{
    /// <summary>
    /// Gets or sets the authentication method identifier.
    /// </summary>
    /// <value>The auth method name. Defaults to <c>token</c>.</value>
    public string Method { get; set; } = "token";

    /// <summary>
    /// Gets or sets the token-based authentication options.
    /// </summary>
    public TokenAuthenticationOptions? Token { get; set; }

    /// <summary>
    /// Gets or sets the AppRole authentication options.
    /// </summary>
    public AppRoleAuthenticationOptions? AppRole { get; set; }

    /// <summary>
    /// Gets or sets the Kubernetes authentication options.
    /// </summary>
    public KubernetesAuthenticationOptions? Kubernetes { get; set; }

    /// <summary>
    /// Gets or sets the AWS IAM authentication options.
    /// </summary>
    public AwsIamAuthenticationOptions? AwsIam { get; set; }

    /// <summary>
    /// Gets or sets the LDAP authentication options.
    /// </summary>
    public LdapAuthenticationOptions? Ldap { get; set; }

    /// <summary>
    /// Gets or sets the TLS certificate authentication options.
    /// </summary>
    public TlsCertificateAuthenticationOptions? TlsCertificate { get; set; }
}

/// <summary>
/// Options for static-token authentication against Vault.
/// </summary>
public class TokenAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the static Vault token used to authenticate.
    /// </summary>
    /// <value>The token. Defaults to <see cref="string.Empty"/>.</value>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an asynchronous callback that produces a Vault token at runtime.
    /// </summary>
    /// <remarks>
    /// When set, this provider is invoked to resolve the token instead of using
    /// <see cref="Token"/>. Useful for integrating with secret brokers or
    /// short-lived token sources.
    /// </remarks>
    /// <value>The token provider delegate, or <see langword="null"/> to use <see cref="Token"/>.</value>
    public Func<Task<string>>? TokenProvider { get; set; }
}

/// <summary>
/// Options for the AppRole authentication method.
/// </summary>
public class AppRoleAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the AppRole <c>role_id</c>.
    /// </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AppRole <c>secret_id</c>.
    /// </summary>
    public string SecretId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount path of the AppRole auth method in Vault.
    /// </summary>
    /// <value>The auth method path. Defaults to <c>approle</c>.</value>
    public string AppRolePath { get; set; } = "approle";

    /// <summary>
    /// Gets or sets a value indicating whether the library should exchange the
    /// role/secret IDs for a Vault token.
    /// </summary>
    /// <value><see langword="true"/> to perform the <c>approle/login</c> exchange; otherwise <see langword="false"/>.</value>
    public bool RetrieveToken { get; set; } = true;
}

/// <summary>
/// Options for the Kubernetes authentication method.
/// </summary>
public class KubernetesAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the name of the Kubernetes auth role to assume.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount path of the Kubernetes auth method in Vault.
    /// </summary>
    /// <value>The auth method path. Defaults to <c>kubernetes</c>.</value>
    public string KubernetesRolePath { get; set; } = "kubernetes";

    /// <summary>
    /// Gets or sets the filesystem path of the projected service account token
    /// used during login.
    /// </summary>
    /// <value>The token file path. Defaults to the standard in-cluster path
    /// <c>/var/run/secrets/kubernetes.io/serviceaccount/token</c>.</value>
    public string ServiceAccountTokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    /// <summary>
    /// Gets or sets the filesystem path of the CA certificate used to verify
    /// the Vault server during login.
    /// </summary>
    /// <value>The CA certificate path. Defaults to the standard in-cluster path
    /// <c>/var/run/secrets/kubernetes.io/serviceaccount/ca.crt</c>.</value>
    public string ServiceAccountCaCertPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
}

/// <summary>
/// Options for the AWS IAM authentication method.
/// </summary>
public class AwsIamAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the Vault role bound to the IAM principal.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount path of the AWS auth method in Vault.
    /// </summary>
    /// <value>The auth method path. Defaults to <c>aws</c>.</value>
    public string AwsPath { get; set; } = "aws";

    /// <summary>
    /// Gets or sets the AWS region the IAM principal resides in.
    /// </summary>
    public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Options for the LDAP authentication method.
/// </summary>
public class LdapAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the LDAP username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the LDAP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mount path of the LDAP auth method in Vault.
    /// </summary>
    /// <value>The auth method path. Defaults to <c>ldap</c>.</value>
    public string LdapPath { get; set; } = "ldap";
}

/// <summary>
/// Options for the TLS certificate (<c>cert</c>) authentication method.
/// </summary>
public class TlsCertificateAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the filesystem path of the client certificate (PEM or PFX).
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filesystem path of the client certificate private key.
    /// </summary>
    public string CertificateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional password for the client certificate's private key.
    /// </summary>
    /// <value>The password, or <see langword="null"/> when the key is unencrypted.</value>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets the mount path of the certificate auth method in Vault.
    /// </summary>
    /// <value>The auth method path. Defaults to <c>cert</c>.</value>
    public string CertAuthPath { get; set; } = "cert";
}

/// <summary>
/// Options for the Key/Value (KV) secret backend.
/// </summary>
public class KvSecretBackendOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the KV backend is enabled.
    /// </summary>
    /// <value><see langword="true"/> to read from KV; otherwise <see langword="false"/>.</value>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the mount path of the KV backend in Vault.
    /// </summary>
    /// <value>The mount path. Defaults to <c>secret</c>.</value>
    public string BackendPath { get; set; } = "secret";

    /// <summary>
    /// Gets or sets the KV engine version (1 or 2).
    /// </summary>
    /// <value>The KV version. Defaults to <c>2</c>.</value>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Gets or sets the optional application name used to scope secret lookups.
    /// </summary>
    /// <value>The application name, or <see langword="null"/> to use the default scope.</value>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Gets or sets the default Vault context (Enterprise) used for secret lookups.
    /// </summary>
    /// <value>The context name. Defaults to <c>application</c>.</value>
    public string DefaultContext { get; set; } = "application";

    /// <summary>
    /// Gets or sets the optional Vault secret backend name (Enterprise).
    /// </summary>
    /// <value>The backend name, or <see langword="null"/> when not using a named backend.</value>
    public string? BackendName { get; set; }

    /// <summary>
    /// Gets or sets the list of active KV profiles to expose.
    /// </summary>
    /// <value>The list of profile names. Defaults to an empty list.</value>
    public List<string> Profiles { get; set; } = new();

    /// <summary>
    /// Gets or sets the separator used to delimit profile segments in secret paths.
    /// </summary>
    /// <value>The separator. Defaults to <c>/</c>.</value>
    public string ProfileSeparator { get; set; } = "/";
}

/// <summary>
/// Options for the database secret backend.
/// </summary>
public class DatabaseSecretBackendOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the database backend is enabled.
    /// </summary>
    /// <value><see langword="true"/> to read dynamic database credentials; otherwise <see langword="false"/>.</value>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the mount path of the database backend in Vault.
    /// </summary>
    /// <value>The mount path. Defaults to <c>database</c>.</value>
    public string BackendPath { get; set; } = "database";

    /// <summary>
    /// Gets or sets the Vault role that maps to the desired database credentials.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional prefix added to keys when materializing the
    /// connection settings into configuration.
    /// </summary>
    /// <value>The property prefix, or <see langword="null"/> for no prefix.</value>
    public string? PropertyPrefix { get; set; }
}

/// <summary>
/// Options for the PKI secret backend.
/// </summary>
public class PkiSecretBackendOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the PKI backend is enabled.
    /// </summary>
    /// <value><see langword="true"/> to issue certificates from PKI; otherwise <see langword="false"/>.</value>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the mount path of the PKI backend in Vault.
    /// </summary>
    /// <value>The mount path. Defaults to <c>pki</c>.</value>
    public string BackendPath { get; set; } = "pki";

    /// <summary>
    /// Gets or sets the Vault role used to issue certificates.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested certificate common name (CN).
    /// </summary>
    public string CommonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested certificate subject alternative names (SANs).
    /// </summary>
    /// <value>The list of SANs. Defaults to an empty list.</value>
    public List<string> AltNames { get; set; } = new();

    /// <summary>
    /// Gets or sets the requested certificate time-to-live.
    /// </summary>
    /// <value>The TTL, or <see langword="null"/> to use the role's default.</value>
    public TimeSpan? Ttl { get; set; }
}

/// <summary>
/// Options that govern background refresh of secrets.
/// </summary>
public class VaultRefreshOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the background refresh loop is enabled.
    /// </summary>
    /// <value><see langword="true"/> to run the refresh loop; otherwise <see langword="false"/>.</value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval between refresh cycles.
    /// </summary>
    /// <value>The refresh interval, or <see langword="null"/> to derive it from secret leases.</value>
    public TimeSpan? Interval { get; set; }

    /// <summary>
    /// Gets or sets the retry policy applied to failed refresh attempts.
    /// </summary>
    public VaultRetryOptions Retry { get; set; } = new();
}

/// <summary>
/// Options for the exponential backoff used when retrying failed operations.
/// </summary>
public class VaultRetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts before giving up.
    /// </summary>
    /// <value>The retry count. Defaults to <c>3</c>.</value>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial delay before the first retry.
    /// </summary>
    /// <value>The initial delay. Defaults to 1 second.</value>
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay between retries.
    /// </summary>
    /// <value>The maximum delay. Defaults to 30 seconds.</value>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the multiplier applied to the delay between consecutive retries.
    /// </summary>
    /// <value>The exponential multiplier. Defaults to <c>2.0</c>.</value>
    public double Multiplier { get; set; } = 2.0;
}
