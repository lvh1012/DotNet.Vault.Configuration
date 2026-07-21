# DotNet.Vault.Configuration Design Specification

**Date**: 2026-07-21  
**Status**: Approved  
**Version**: 1.0

## Overview

DotNet.Vault.Configuration là một .NET extension library cho `IConfiguration` để tích hợp HashiCorp Vault, tương tự spring-cloud-vault. Library cung cấp seamless integration với Vault để quản lý secrets, credentials, và configuration trong distributed systems.

## Requirements

### Functional Requirements

1. **Authentication Methods**: Support nhiều authentication methods:
   - Token (static và dynamic)
   - AppRole (machine-to-machine)
   - Kubernetes (service account)
   - AWS IAM (EC2, ECS, Lambda)
   - LDAP/Username-Password
   - TLS Certificates

2. **Secret Engines**: Support đầy đủ secret engines:
   - KV Secrets Engine (v1 và v2)
   - Database Secrets Engine (dynamic credentials)
   - PKI Secrets Engine (certificates)

3. **Configuration Integration**: 
   - Implement `IConfigurationProvider` và `IConfigurationSource`
   - Path strategy: Application name + profiles (Spring Cloud Vault style)
   - Configuration binding và strongly-typed options

4. **TTL Monitoring và Refresh**:
   - Periodic refresh với TTL monitoring
   - Automatic renewal cho renewable secrets
   - Re-fetch cho non-renewable secrets
   - Exponential backoff retry policy

5. **Health Check**:
   - Tích hợp với `Microsoft.Extensions.Diagnostics.HealthChecks`
   - Monitor Vault connectivity, authentication, và secret freshness

6. **Error Handling**:
   - Fail-fast mode (default) hoặc graceful degradation
   - Custom exceptions cho từng error scenario
   - Structured logging với ILogger

### Non-Functional Requirements

1. **Performance**: Minimal overhead, connection pooling
2. **Scalability**: Support cluster mode, standby nodes
3. **Security**: Secure token management, no secret leakage in logs
4. **Testability**: High test coverage (80%+ unit tests)
5. **Extensibility**: Plugin architecture cho custom auth methods và secret backends

## Architecture

### Design Pattern

**Plugin Architecture (Approach 3)** - Core package với plugin interfaces, auth methods và secret engines là plugins register qua DI.

**Rationale**:
- Extensible - Dễ thêm auth methods/secret engines mới
- Testable - Mock interfaces để test isolated
- Flexible - Ship tất cả trong 1 package ban đầu, tách ra sau nếu cần
- Industry standard - Tương tự ASP.NET Core patterns

### High-Level Components

```
Application
    ↓
IConfiguration
    ↓
VaultConfigurationProvider (IConfigurationProvider)
    ↓
VaultConfigurationSource (IConfigurationSource)
    ↓
VaultClient (HTTP client wrapper)
    ├── IVaultAuthenticationProvider (plugin)
    │   ├── TokenAuthProvider
    │   ├── AppRoleAuthProvider
    │   ├── KubernetesAuthProvider
    │   ├── AwsIamAuthProvider
    │   ├── LdapAuthProvider
    │   └── TlsCertificateAuthProvider
    ├── IVaultSecretBackend (plugin)
    │   ├── KvSecretBackend (v1/v2)
    │   ├── DatabaseSecretBackend
    │   └── PkiSecretBackend
    ├── SecretRefresher (TTL monitoring)
    └── VaultHealthCheck (IHealthCheck)
    ↓
HashiCorp Vault Server
```

### Component Responsibilities

1. **VaultConfigurationProvider**: Bridge giữa Vault và .NET configuration system
2. **VaultConfigurationSource**: Factory cho provider, register services
3. **VaultClient**: HTTP client wrapper để giao tiếp với Vault API
4. **IVaultAuthenticationProvider**: Interface cho authentication strategies
5. **IVaultSecretBackend**: Interface cho secret engine implementations
6. **SecretRefresher**: Background service để refresh secrets theo TTL
7. **VaultHealthCheck**: Monitor Vault connectivity và health

## Authentication Providers

### IVaultAuthenticationProvider Interface

```csharp
public interface IVaultAuthenticationProvider
{
    string AuthenticationMethod { get; }
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default);
}
```

### Built-in Providers

#### 1. TokenAuthProvider

**Configuration**:
```csharp
public class TokenAuthenticationOptions
{
    public string Token { get; set; }  // Static token
    public Func<Task<string>> TokenProvider { get; set; }  // Dynamic token fetcher
}
```

**Use case**: Dev/testing, hoặc khi có external token management

#### 2. AppRoleAuthProvider

**Configuration**:
```csharp
public class AppRoleAuthenticationOptions
{
    public string RoleId { get; set; }
    public string SecretId { get; set; }
    public string AppRolePath { get; set; } = "approle";
    public bool RetrieveToken { get; set; } = true;
}
```

**Use case**: Machine-to-machine authentication, services  
**Lifecycle**: Login → nhận token → cache → renew trước khi expire

#### 3. KubernetesAuthProvider

**Configuration**:
```csharp
public class KubernetesAuthenticationOptions
{
    public string Role { get; set; }
    public string KubernetesRolePath { get; set; } = "kubernetes";
    public string ServiceAccountTokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public string ServiceAccountCaCertPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
}
```

**Use case**: Applications chạy trong Kubernetes cluster

#### 4. AwsIamAuthProvider

**Configuration**:
```csharp
public class AwsIamAuthenticationOptions
{
    public string Role { get; set; }
    public string AwsPath { get; set; } = "aws";
    public string Region { get; set; }
    public AwsCredentials? Credentials { get; set; }
}
```

**Use case**: Applications trên AWS (EC2, ECS, Lambda)

#### 5. LdapAuthProvider

**Configuration**:
```csharp
public class LdapAuthenticationOptions
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string LdapPath { get; set; } = "ldap";
}
```

**Use case**: Enterprise environments với LDAP/AD

#### 6. TlsCertificateAuthProvider

**Configuration**:
```csharp
public class TlsCertificateAuthenticationOptions
{
    public string CertificatePath { get; set; }
    public string CertificateKeyPath { get; set; }
    public string CertificatePassword { get; set; }
    public string CertAuthPath { get; set; } = "cert";
}
```

**Use case**: High-security environments, zero-trust architectures

### Token Management

- **Caching**: Auth providers cache tokens internally
- **Renewal**: Trước khi token expire (theo TTL từ Vault)
- **Thread-safety**: Sử dụng `SemaphoreSlim` hoặc `AsyncLazy` để tránh race conditions
- **Fallback**: Nếu renewal fail, re-authenticate

## Secret Backends

### IVaultSecretBackend Interface

```csharp
public interface IVaultSecretBackend
{
    string BackendType { get; }
    Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default);
    bool CanHandle(string path);
    TimeSpan? GetTtl(string path);
}

public class SecretRequest
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class SecretResult
{
    public Dictionary<string, string> Secrets { get; set; } = new();
    public string? LeaseId { get; set; }
    public TimeSpan? LeaseDuration { get; set; }
    public bool Renewable { get; set; }
    public DateTimeOffset? ExpireTime { get; set; }
}
```

### Built-in Backends

#### 1. KvSecretBackend (v1 + v2)

**Configuration**:
```csharp
public class KvSecretBackendOptions
{
    public string BackendPath { get; set; } = "secret";
    public int Version { get; set; } = 2;
    public string? ApplicationName { get; set; }
    public string DefaultContext { get; set; } = "application";
    public string? BackendName { get; set; }
    public List<string> Profiles { get; set; } = new();
    public string ProfileSeparator { get; set; } = "/";
}
```

**Secret Path Strategy** (Spring Cloud Vault style):

```
# Với ApplicationName = "myapp", Profiles = ["dev", "prod"], BackendPath = "secret"
# KV v2 paths:

secret/data/application           # Default context
secret/data/application/dev       # Default context + profile
secret/data/application/prod      # Default context + profile
secret/data/myapp                 # Application name
secret/data/myapp/dev             # Application name + profile
secret/data/myapp/prod            # Application name + profile

# Priority order (later overrides earlier):
1. secret/data/application
2. secret/data/application/dev
3. secret/data/application/prod
4. secret/data/myapp
5. secret/data/myapp/dev
6. secret/data/myapp/prod
```

**KV v1 vs v2**:
- **v1**: `secret/myapp` → direct path
- **v2**: `secret/data/myapp` → có prefix `data/`, metadata ở `secret/metadata/myapp`

#### 2. DatabaseSecretBackend

**Configuration**:
```csharp
public class DatabaseSecretBackendOptions
{
    public string BackendPath { get; set; } = "database";
    public string Role { get; set; } = string.Empty;
    public string? PropertyPrefix { get; set; }
}
```

**Behavior**:
- Generate dynamic database credentials (username, password)
- Credentials có TTL, tự động renew/rotate
- Map vào configuration keys:
  ```
  spring.datasource.username = v-token-myapp-abc123
  spring.datasource.password = A1b2C3d4E5f6
  ```

#### 3. PkiSecretBackend

**Configuration**:
```csharp
public class PkiSecretBackendOptions
{
    public string BackendPath { get; set; } = "pki";
    public string Role { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public List<string> AltNames { get; set; } = new();
    public TimeSpan? Ttl { get; set; }
}
```

**Behavior**:
- Generate X.509 certificates on-demand
- Certificates có TTL, tự động renew
- Map vào configuration keys:
  ```
  myapp.certificate.pem = -----BEGIN CERTIFICATE-----...
  myapp.certificate.key = -----BEGIN RSA PRIVATE KEY-----...
  myapp.certificate.ca_chain = -----BEGIN CERTIFICATE-----...
  ```

## Configuration Integration

### VaultConfigurationProvider

```csharp
public class VaultConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly VaultClient _client;
    private readonly VaultOptions _options;
    private readonly SecretRefresher _refresher;
    private readonly ILogger _logger;
    private Timer? _refreshTimer;
    
    public override void Load()
    {
        LoadAsync().GetAwaiter().GetResult();
    }
    
    private async Task LoadAsync()
    {
        try
        {
            var paths = BuildSecretPaths();
            var secrets = await _client.LoadSecretsAsync(paths);
            Data = secrets;
            SetupRefreshIfNeeded();
            _logger.LogInformation("Loaded {Count} secrets from Vault", secrets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load secrets from Vault");
            if (_options.FailFast)
                throw;
        }
    }
}
```

### Extension Methods

```csharp
public static class VaultConfigurationExtensions
{
    public static IConfigurationBuilder AddVault(
        this IConfigurationBuilder builder,
        Action<VaultOptions> configure)
    {
        var source = new VaultConfigurationSource();
        configure(source.Options);
        builder.Add(source);
        return builder;
    }
    
    public static IConfigurationBuilder AddVault(
        this IConfigurationBuilder builder,
        IConfiguration configuration)
    {
        var source = new VaultConfigurationSource();
        configuration.Bind(source.Options);
        builder.Add(source);
        return builder;
    }
}
```

### VaultOptions

```csharp
public class VaultOptions
{
    public Uri Uri { get; set; } = new Uri("http://localhost:8200");
    public string? Namespace { get; set; }
    public VaultSslOptions Ssl { get; set; } = new();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public VaultAuthenticationConfiguration Authentication { get; set; } = new();
    public KvSecretBackendOptions Kv { get; set; } = new();
    public DatabaseSecretBackendOptions Database { get; set; } = new();
    public PkiSecretBackendOptions Pki { get; set; } = new();
    public VaultRefreshOptions Refresh { get; set; } = new();
    public bool FailFast { get; set; } = true;
}
```

## TTL Monitoring và Refresh

### SecretRefresher

```csharp
public class SecretRefresher : IDisposable, IHostedService
{
    private readonly Dictionary<string, SecretMetadata> _secretMetadata = new();
    
    public void TrackSecret(string path, SecretResult result)
    {
        if (result.LeaseDuration.HasValue || result.ExpireTime.HasValue)
        {
            _secretMetadata[path] = new SecretMetadata
            {
                Path = path,
                LeaseId = result.LeaseId,
                LeaseDuration = result.LeaseDuration,
                ExpireTime = result.ExpireTime,
                Renewable = result.Renewable,
                LastRefreshed = DateTimeOffset.UtcNow
            };
        }
    }
    
    public bool ShouldRefresh()
    {
        // Refresh nếu bất kỳ secret nào sắp expire (trong vòng 20% TTL)
        return _secretMetadata.Values.Any(m => 
        {
            if (!m.LeaseDuration.HasValue)
                return false;
            
            var timeUntilExpiry = m.ExpireTime ?? m.LastRefreshed.Add(m.LeaseDuration.Value);
            var timeRemaining = timeUntilExpiry - DateTimeOffset.UtcNow;
            var threshold = m.LeaseDuration.Value * 0.2; // 20% threshold
            
            return timeRemaining < threshold;
        });
    }
    
    private async Task RefreshLoopAsync()
    {
        // Renew lease cho renewable secrets
        await RenewLeasesAsync();
        
        // Re-fetch non-renewable secrets
        await RefetchExpiredSecretsAsync();
    }
}
```

### Refresh Behavior

1. **TTL Tracking**: Mỗi secret có TTL được track trong `SecretRefresher`
2. **Refresh Threshold**: Refresh khi còn 20% TTL (configurable)
3. **Renewal**: Renewable secrets (leases) → renew qua Vault API
4. **Re-fetch**: Non-renewable secrets → fetch lại từ Vault
5. **Retry**: Exponential backoff khi refresh fail (configurable)
6. **Notification**: Notify `IConfigurationProvider` để reload data
7. **Thread-safety**: `_isRefreshing` flag để tránh concurrent refresh

## Health Check Integration

### VaultHealthCheck

```csharp
public class VaultHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Check Vault connectivity
        var vaultHealth = await _client.GetHealthAsync(cancellationToken);
        
        if (!vaultHealth.Initialized)
            return HealthCheckResult.Unhealthy("Vault is not initialized");
        
        if (vaultHealth.Sealed)
            return HealthCheckResult.Unhealthy("Vault is sealed");
        
        // 2. Check authentication
        var isAuthValid = await _client.IsAuthenticationValidAsync(cancellationToken);
        if (!isAuthValid)
            return HealthCheckResult.Degraded("Vault authentication is invalid or expired");
        
        // 3. Check secret freshness
        var minTtl = _refresher.GetMinimumTtl();
        var data = new Dictionary<string, object>
        {
            ["vault_version"] = vaultHealth.Version,
            ["vault_cluster"] = vaultHealth.ClusterName
        };
        
        if (minTtl.HasValue && minTtl.Value < TimeSpan.FromMinutes(5))
            return HealthCheckResult.Degraded("Some secrets are expiring soon", data: data);
        
        return HealthCheckResult.Healthy("Vault is healthy", data);
    }
}
```

### Registration

```csharp
builder.Services.AddHealthChecks()
    .AddVault(
        name: "vault",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "vault" },
        timeout: TimeSpan.FromSeconds(5));
```

## Error Handling

### Custom Exceptions

```csharp
public class VaultException : Exception { }
public class VaultConnectionException : VaultException { }
public class VaultAuthenticationException : VaultException { }
public class VaultApiException : VaultException { }
public class VaultSealedException : VaultException { }
public class VaultSecretNotFoundException : VaultException { }
public class VaultBackendNotSupportedException : VaultException { }
public class VaultLeaseRenewalException : VaultException { }
```

### Error Handling Strategy

1. **Fail-fast vs Graceful**: Configurable behavior qua `options.FailFast`
2. **Retry Policy**: Exponential backoff cho transient failures (Polly integration)
3. **Logging**: Structured logging với scope, log levels phù hợp
4. **Health Check**: Expose error status qua health endpoint

### Retry Configuration

```csharp
public class VaultRetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);
    public double Multiplier { get; set; } = 2.0;
}
```

## Testing Strategy

### Test Categories

1. **Unit Tests**: Test individual components trong isolation (80%+ coverage)
2. **Integration Tests**: Test với Vault server thật (dev mode)
3. **End-to-End Tests**: Test full configuration flow

### Test Project Structure

```
tests/
├── DotNet.Vault.Configuration.Tests/
│   ├── Unit/
│   │   ├── Authentication/
│   │   ├── Backends/
│   │   └── Configuration/
│   ├── Integration/
│   │   ├── VaultClientIntegrationTests.cs
│   │   └── VaultFixture.cs
│   └── EndToEnd/
│       └── VaultConfigurationE2ETests.cs
└── DotNet.Vault.Configuration.Tests.csproj
```

### Mocking Strategy

```csharp
public class MockVaultClient : VaultClient
{
    private readonly Dictionary<string, SecretResult> _secrets = new();
    
    public void SetupSecret(string path, Dictionary<string, string> data)
    {
        _secrets[path] = new SecretResult { Secrets = data };
    }
    
    public override Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        if (_secrets.TryGetValue(request.Path, out var result))
            return Task.FromResult(result);
        
        throw new VaultSecretNotFoundException(request.Path);
    }
}
```

## Usage Examples

### Basic Usage

```csharp
var builder = new ConfigurationBuilder()
    .AddVault(options =>
    {
        options.Uri = new Uri("http://localhost:8200");
        options.Authentication.Method = "token";
        options.Authentication.Token = new TokenAuthenticationOptions
        {
            Token = "myroot"
        };
        options.Kv.Enabled = true;
        options.Kv.BackendPath = "secret";
        options.Kv.Version = 2;
        options.Kv.ApplicationName = "myapp";
    });

var configuration = builder.Build();

var connectionString = configuration["ConnectionStrings:DefaultConnection"];
var apiKey = configuration["ExternalServices:ApiKey"];
```

### Advanced Usage

```csharp
services.AddVault(options =>
{
    options.Uri = new Uri("https://vault.example.com");
    
    // AppRole authentication
    options.Authentication.Method = "approle";
    options.Authentication.AppRole = new AppRoleAuthenticationOptions
    {
        RoleId = "my-role-id",
        SecretId = "my-secret-id"
    };
    
    // KV backend
    options.Kv.Enabled = true;
    options.Kv.BackendPath = "secret";
    options.Kv.Version = 2;
    options.Kv.ApplicationName = "myapp";
    options.Kv.Profiles = new List<string> { "dev", "prod" };
    
    // Database backend
    options.Database.Enabled = true;
    options.Database.BackendPath = "database";
    options.Database.Role = "myapp-role";
    options.Database.PropertyPrefix = "spring.datasource";
    
    // PKI backend
    options.Pki.Enabled = true;
    options.Pki.BackendPath = "pki";
    options.Pki.Role = "myapp-role";
    options.Pki.CommonName = "myapp.example.com";
    
    // Refresh configuration
    options.Refresh.Enabled = true;
    options.Refresh.Interval = TimeSpan.FromMinutes(5);
    options.Refresh.Retry = new VaultRetryOptions
    {
        MaxRetries = 3,
        InitialInterval = TimeSpan.FromSeconds(1),
        MaxInterval = TimeSpan.FromSeconds(30),
        Multiplier = 2.0
    };
    
    // Fail-fast
    options.FailFast = true;
});
```

### Health Check

```csharp
builder.Services.AddHealthChecks()
    .AddVault(
        name: "vault",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "vault" },
        timeout: TimeSpan.FromSeconds(5));

app.MapHealthChecks("/health");
```

## Technology Stack

- **.NET 10.0** - Target framework
- **Microsoft.Extensions.Configuration** - IConfiguration integration
- **System.Net.Http** - HTTP client
- **Polly** - Retry policies (optional)
- **Microsoft.Extensions.Diagnostics.HealthChecks** - Health check integration
- **xUnit + Moq** - Testing

## Future Enhancements

1. **Additional Secret Engines**: Consul, Terraform Cloud, etc.
2. **Vault Agent Integration**: Support Vault Agent sidecar pattern
3. **Secret Versioning**: Support secret version history và rollback
4. **Metrics**: Prometheus/OpenTelemetry integration
5. **Distributed Caching**: Cache secrets trong distributed environment

## Appendix

### Glossary

- **Secret**: Sensitive data (passwords, API keys, certificates)
- **Lease**: Time-limited access to a secret
- **TTL**: Time-to-live, duration before secret expires
- **Auth Method**: Authentication strategy (Token, AppRole, K8s, etc.)
- **Secret Engine**: Vault component that manages secrets (KV, Database, PKI)
- **Fail-fast**: Strategy where application fails immediately if Vault unavailable

### References

- [Spring Cloud Vault Documentation](https://docs.spring.io/spring-cloud-vault/reference/index.html)
- [HashiCorp Vault Documentation](https://developer.hashicorp.com/vault/docs)
- [.NET Configuration Providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)
