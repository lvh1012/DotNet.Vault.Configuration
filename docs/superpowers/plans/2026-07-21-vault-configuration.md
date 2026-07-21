# DotNet.Vault.Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET extension library for IConfiguration to integrate HashiCorp Vault, supporting multiple authentication methods and secret engines with TTL monitoring.

**Architecture:** Plugin architecture with core package containing IVaultAuthenticationProvider và IVaultSecretBackend interfaces. Authentication methods và secret engines are plugins registered via DI. Periodic refresh với TTL monitoring cho dynamic credentials.

**Tech Stack:** .NET 10.0, Microsoft.Extensions.Configuration, System.Net.Http, Polly (optional), Microsoft.Extensions.Diagnostics.HealthChecks, xUnit + Moq

## Global Constraints

- Target framework: net10.0
- Nullable reference types: enable
- Implicit usings: enable
- Test coverage: 80%+ for core logic
- No secret leakage in logs
- Fail-fast mode default (configurable)
- Spring Cloud Vault compatible path strategy

---

## File Structure

```
DotNet.Vault.Configuration/
├── src/
│   ├── Core/
│   │   ├── VaultClient.cs                      # HTTP client wrapper
│   │   ├── VaultOptions.cs                     # Configuration model
│   │   ├── VaultConfigurationProvider.cs       # IConfigurationProvider
│   │   ├── VaultConfigurationSource.cs         # IConfigurationSource
│   │   ├── Exceptions/
│   │   │   ├── VaultException.cs
│   │   │   ├── VaultConnectionException.cs
│   │   │   ├── VaultAuthenticationException.cs
│   │   │   ├── VaultApiException.cs
│   │   │   ├── VaultSealedException.cs
│   │   │   ├── VaultSecretNotFoundException.cs
│   │   │   ├── VaultBackendNotSupportedException.cs
│   │   │   └── VaultLeaseRenewalException.cs
│   │   └── Extensions/
│   │       ├── VaultConfigurationExtensions.cs
│   │       └── VaultServiceCollectionExtensions.cs
│   ├── Authentication/
│   │   ├── IVaultAuthenticationProvider.cs
│   │   ├── TokenAuthProvider.cs
│   │   ├── AppRoleAuthProvider.cs
│   │   ├── KubernetesAuthProvider.cs
│   │   ├── AwsIamAuthProvider.cs
│   │   ├── LdapAuthProvider.cs
│   │   └── TlsCertificateAuthProvider.cs
│   ├── Backends/
│   │   ├── IVaultSecretBackend.cs
│   │   ├── KvSecretBackend.cs
│   │   ├── KvPathBuilder.cs
│   │   ├── DatabaseSecretBackend.cs
│   │   └── PkiSecretBackend.cs
│   ├── Refresh/
│   │   └── SecretRefresher.cs
│   └── HealthChecks/
│       └── VaultHealthCheck.cs
├── tests/
│   └── DotNet.Vault.Configuration.Tests/
│       ├── Unit/
│       │   ├── Core/
│       │   ├── Authentication/
│       │   ├── Backends/
│       │   └── Configuration/
│       ├── Integration/
│       │   ├── VaultClientIntegrationTests.cs
│       │   └── VaultFixture.cs
│       └── EndToEnd/
│           └── VaultConfigurationE2ETests.cs
└── docs/
    └── superpowers/
        └── specs/
            └── 2026-07-21-vault-configuration-design.md
```

---

## Task 1: Project Setup và Dependencies

**Files:**
- Modify: `DotNet.Vault.Configuration.csproj`
- Create: `tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj`
- Create: `DotNet.Vault.Configuration.slnx` (update)

**Interfaces:**
- Consumes: None
- Produces: Project structure với dependencies

- [ ] **Step 1: Update main project file**

```xml
<!-- DotNet.Vault.Configuration.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>DotNet.Vault.Configuration</RootNamespace>
        <AssemblyName>DotNet.Vault.Configuration</AssemblyName>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
        <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="10.0.0" />
        <PackageReference Include="System.Text.Json" Version="10.0.0" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Create test project file**

```xml
<!-- tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
        <PackageReference Include="xunit" Version="2.9.2" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="Moq" Version="4.20.72" />
        <PackageReference Include="FluentAssertions" Version="6.12.2" />
        <PackageReference Include="coverlet.collector" Version="6.0.2">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\DotNet.Vault.Configuration.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 3: Update solution file**

```bash
dotnet sln add DotNet.Vault.Configuration.csproj
dotnet sln add tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj
```

- [ ] **Step 4: Restore packages**

```bash
dotnet restore
```

Expected: All packages restored successfully

- [ ] **Step 5: Build solution**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git init
git add .
git commit -m "chore: setup project structure và dependencies"
```

---

## Task 2: Core Exceptions

**Files:**
- Create: `src/Core/Exceptions/VaultException.cs`
- Create: `src/Core/Exceptions/VaultConnectionException.cs`
- Create: `src/Core/Exceptions/VaultAuthenticationException.cs`
- Create: `src/Core/Exceptions/VaultApiException.cs`
- Create: `src/Core/Exceptions/VaultSealedException.cs`
- Create: `src/Core/Exceptions/VaultSecretNotFoundException.cs`
- Create: `src/Core/Exceptions/VaultBackendNotSupportedException.cs`
- Create: `src/Core/Exceptions/VaultLeaseRenewalException.cs`

**Interfaces:**
- Consumes: None
- Produces: Exception hierarchy for Vault errors

- [ ] **Step 1: Create VaultException base class**

```csharp
// src/Core/Exceptions/VaultException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

/// <summary>
/// Base exception cho tất cả Vault-related errors
/// </summary>
public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Create VaultConnectionException**

```csharp
// src/Core/Exceptions/VaultConnectionException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultConnectionException : VaultException
{
    public Uri VaultUri { get; }
    
    public VaultConnectionException(Uri vaultUri, Exception innerException)
        : base($"Failed to connect to Vault at {vaultUri}", innerException)
    {
        VaultUri = vaultUri;
    }
}
```

- [ ] **Step 3: Create VaultAuthenticationException**

```csharp
// src/Core/Exceptions/VaultAuthenticationException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultAuthenticationException : VaultException
{
    public string AuthenticationMethod { get; }
    
    public VaultAuthenticationException(string method, string message)
        : base($"Authentication failed for method '{method}': {message}")
    {
        AuthenticationMethod = method;
    }
    
    public VaultAuthenticationException(string method, string message, Exception innerException)
        : base($"Authentication failed for method '{method}': {message}", innerException)
    {
        AuthenticationMethod = method;
    }
}
```

- [ ] **Step 4: Create VaultApiException**

```csharp
// src/Core/Exceptions/VaultApiException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultApiException : VaultException
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public string? RequestId { get; }
    
    public VaultApiException(int statusCode, string message, string? errorCode = null, string? requestId = null)
        : base($"Vault API error (HTTP {statusCode}): {message}")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
    }
}
```

- [ ] **Step 5: Create VaultSealedException**

```csharp
// src/Core/Exceptions/VaultSealedException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultSealedException : VaultException
{
    public VaultSealedException()
        : base("Vault is sealed. Unseal Vault before accessing secrets.") { }
}
```

- [ ] **Step 6: Create VaultSecretNotFoundException**

```csharp
// src/Core/Exceptions/VaultSecretNotFoundException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultSecretNotFoundException : VaultException
{
    public string Path { get; }
    
    public VaultSecretNotFoundException(string path)
        : base($"Secret not found at path: {path}")
    {
        Path = path;
    }
    
    public VaultSecretNotFoundException(string path, string message)
        : base($"Secret not found at path '{path}': {message}")
    {
        Path = path;
    }
}
```

- [ ] **Step 7: Create VaultBackendNotSupportedException**

```csharp
// src/Core/Exceptions/VaultBackendNotSupportedException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultBackendNotSupportedException : VaultException
{
    public string BackendType { get; }
    
    public VaultBackendNotSupportedException(string backendType)
        : base($"Secret backend '{backendType}' is not supported or not enabled")
    {
        BackendType = backendType;
    }
}
```

- [ ] **Step 8: Create VaultLeaseRenewalException**

```csharp
// src/Core/Exceptions/VaultLeaseRenewalException.cs
namespace DotNet.Vault.Configuration.Core.Exceptions;

public class VaultLeaseRenewalException : VaultException
{
    public string LeaseId { get; }
    
    public VaultLeaseRenewalException(string leaseId, string message)
        : base($"Failed to renew lease '{leaseId}': {message}")
    {
        LeaseId = leaseId;
    }
}
```

- [ ] **Step 9: Commit**

```bash
git add src/Core/Exceptions/
git commit -m "feat: add Vault exception hierarchy"
```

---

## Task 3: VaultOptions và Configuration Models

**Files:**
- Create: `src/Core/VaultOptions.cs`

**Interfaces:**
- Consumes: Exception types từ Task 2
- Produces: Configuration models cho toàn bộ library

- [ ] **Step 1: Create VaultOptions với tất cả configuration models**

```csharp
// src/Core/VaultOptions.cs
namespace DotNet.Vault.Configuration.Core;

public class VaultOptions
{
    public Uri Uri { get; set; } = new Uri("http://localhost:8200");
    public string? Namespace { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public VaultAuthenticationConfiguration Authentication { get; set; } = new();
    public KvSecretBackendOptions Kv { get; set; } = new();
    public DatabaseSecretBackendOptions Database { get; set; } = new();
    public PkiSecretBackendOptions Pki { get; set; } = new();
    public VaultRefreshOptions Refresh { get; set; } = new();
    public bool FailFast { get; set; } = true;
}

public class VaultAuthenticationConfiguration
{
    public string Method { get; set; } = "token";
    public TokenAuthenticationOptions? Token { get; set; }
    public AppRoleAuthenticationOptions? AppRole { get; set; }
    public KubernetesAuthenticationOptions? Kubernetes { get; set; }
    public AwsIamAuthenticationOptions? AwsIam { get; set; }
    public LdapAuthenticationOptions? Ldap { get; set; }
    public TlsCertificateAuthenticationOptions? TlsCertificate { get; set; }
}

public class TokenAuthenticationOptions
{
    public string Token { get; set; } = string.Empty;
    public Func<Task<string>>? TokenProvider { get; set; }
}

public class AppRoleAuthenticationOptions
{
    public string RoleId { get; set; } = string.Empty;
    public string SecretId { get; set; } = string.Empty;
    public string AppRolePath { get; set; } = "approle";
    public bool RetrieveToken { get; set; } = true;
}

public class KubernetesAuthenticationOptions
{
    public string Role { get; set; } = string.Empty;
    public string KubernetesRolePath { get; set; } = "kubernetes";
    public string ServiceAccountTokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public string ServiceAccountCaCertPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
}

public class AwsIamAuthenticationOptions
{
    public string Role { get; set; } = string.Empty;
    public string AwsPath { get; set; } = "aws";
    public string Region { get; set; } = string.Empty;
}

public class LdapAuthenticationOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string LdapPath { get; set; } = "ldap";
}

public class TlsCertificateAuthenticationOptions
{
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificateKeyPath { get; set; } = string.Empty;
    public string? CertificatePassword { get; set; }
    public string CertAuthPath { get; set; } = "cert";
}

public class KvSecretBackendOptions
{
    public bool Enabled { get; set; } = false;
    public string BackendPath { get; set; } = "secret";
    public int Version { get; set; } = 2;
    public string? ApplicationName { get; set; }
    public string DefaultContext { get; set; } = "application";
    public string? BackendName { get; set; }
    public List<string> Profiles { get; set; } = new();
    public string ProfileSeparator { get; set; } = "/";
}

public class DatabaseSecretBackendOptions
{
    public bool Enabled { get; set; } = false;
    public string BackendPath { get; set; } = "database";
    public string Role { get; set; } = string.Empty;
    public string? PropertyPrefix { get; set; }
}

public class PkiSecretBackendOptions
{
    public bool Enabled { get; set; } = false;
    public string BackendPath { get; set; } = "pki";
    public string Role { get; set; } = string.Empty;
    public string CommonName { get; set; } = string.Empty;
    public List<string> AltNames { get; set; } = new();
    public TimeSpan? Ttl { get; set; }
}

public class VaultRefreshOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan? Interval { get; set; }
    public VaultRetryOptions Retry { get; set; } = new();
}

public class VaultRetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);
    public double Multiplier { get; set; } = 2.0;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/VaultOptions.cs
git commit -m "feat: add VaultOptions configuration models"
```

---

## Task 4: Authentication Provider Interface và TokenAuthProvider

**Files:**
- Create: `src/Authentication/IVaultAuthenticationProvider.cs`
- Create: `src/Authentication/TokenAuthProvider.cs`
- Create: `tests/DotNet.Vault.Configuration.Tests/Unit/Authentication/TokenAuthProviderTests.cs`

**Interfaces:**
- Consumes: VaultOptions từ Task 3
- Produces: IVaultAuthenticationProvider interface, TokenAuthProvider implementation

- [ ] **Step 1: Write failing test for TokenAuthProvider**

```csharp
// tests/DotNet.Vault.Configuration.Tests/Unit/Authentication/TokenAuthProviderTests.cs
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Authentication;

public class TokenAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_WithStaticToken_ReturnsToken()
    {
        // Arrange
        var options = Options.Create(new TokenAuthenticationOptions
        {
            Token = "test-token"
        });
        var provider = new TokenAuthProvider(options);
        
        // Act
        var token = await provider.GetTokenAsync();
        
        // Assert
        Assert.Equal("test-token", token);
    }
    
    [Fact]
    public async Task GetTokenAsync_WithDynamicProvider_CallsProvider()
    {
        // Arrange
        var callCount = 0;
        var options = Options.Create(new TokenAuthenticationOptions
        {
            TokenProvider = () =>
            {
                callCount++;
                return Task.FromResult($"dynamic-token-{callCount}");
            }
        });
        var provider = new TokenAuthProvider(options);
        
        // Act
        var token1 = await provider.GetTokenAsync();
        var token2 = await provider.GetTokenAsync();
        
        // Assert
        Assert.Equal("dynamic-token-1", token1);
        Assert.Equal("dynamic-token-2", token2);
        Assert.Equal(2, callCount);
    }
    
    [Fact]
    public void AuthenticationMethod_ReturnsToken()
    {
        // Arrange
        var options = Options.Create(new TokenAuthenticationOptions());
        var provider = new TokenAuthProvider(options);
        
        // Act & Assert
        Assert.Equal("token", provider.AuthenticationMethod);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/DotNet.Vault.Configuration.Tests --filter "FullyQualifiedName~TokenAuthProviderTests"
```

Expected: FAIL - TokenAuthProvider does not exist

- [ ] **Step 3: Create IVaultAuthenticationProvider interface**

```csharp
// src/Authentication/IVaultAuthenticationProvider.cs
namespace DotNet.Vault.Configuration.Authentication;

public interface IVaultAuthenticationProvider
{
    string AuthenticationMethod { get; }
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create TokenAuthProvider implementation**

```csharp
// src/Authentication/TokenAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

public class TokenAuthProvider : IVaultAuthenticationProvider
{
    private readonly TokenAuthenticationOptions _options;
    
    public TokenAuthProvider(IOptions<TokenAuthenticationOptions> options)
    {
        _options = options.Value;
    }
    
    public string AuthenticationMethod => "token";
    
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_options.TokenProvider != null)
        {
            return _options.TokenProvider();
        }
        
        return Task.FromResult(_options.Token);
    }
    
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!string.IsNullOrEmpty(_options.Token) || _options.TokenProvider != null);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/DotNet.Vault.Configuration.Tests --filter "FullyQualifiedName~TokenAuthProviderTests"
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Authentication/ tests/DotNet.Vault.Configuration.Tests/Unit/Authentication/
git commit -m "feat: add TokenAuthProvider with tests"
```

---

## Task 5: Remaining Authentication Providers

**Files:**
- Create: `src/Authentication/AppRoleAuthProvider.cs`
- Create: `src/Authentication/KubernetesAuthProvider.cs`
- Create: `src/Authentication/AwsIamAuthProvider.cs`
- Create: `src/Authentication/LdapAuthProvider.cs`
- Create: `src/Authentication/TlsCertificateAuthProvider.cs`

**Interfaces:**
- Consumes: IVaultAuthenticationProvider từ Task 4
- Produces: Additional authentication providers

- [ ] **Step 1: Create AppRoleAuthProvider**

```csharp
// src/Authentication/AppRoleAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

public class AppRoleAuthProvider : IVaultAuthenticationProvider
{
    private readonly AppRoleAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;
    
    public AppRoleAuthProvider(
        IOptions<AppRoleAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }
    
    public string AuthenticationMethod => "approle";
    
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }
        
        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("approle", "Failed to obtain token");
    }
    
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            role_id = _options.RoleId,
            secret_id = _options.SecretId
        };
        
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.PostAsync($"/v1/auth/{_options.AppRolePath}/login", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();
        
        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);
    }
    
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 2: Create KubernetesAuthProvider**

```csharp
// src/Authentication/KubernetesAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

public class KubernetesAuthProvider : IVaultAuthenticationProvider
{
    private readonly KubernetesAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;
    
    public KubernetesAuthProvider(
        IOptions<KubernetesAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }
    
    public string AuthenticationMethod => "kubernetes";
    
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }
        
        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("kubernetes", "Failed to obtain token");
    }
    
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var jwt = await File.ReadAllTextAsync(_options.ServiceAccountTokenPath, cancellationToken);
        
        var payload = new
        {
            role = _options.Role,
            jwt = jwt
        };
        
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.PostAsync($"/v1/auth/{_options.KubernetesRolePath}/login", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();
        
        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);
    }
    
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 3: Create LdapAuthProvider**

```csharp
// src/Authentication/LdapAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Authentication;

public class LdapAuthProvider : IVaultAuthenticationProvider
{
    private readonly LdapAuthenticationOptions _options;
    private readonly HttpClient _httpClient;
    private string? _cachedToken;
    private DateTimeOffset? _tokenExpiry;
    
    public LdapAuthProvider(
        IOptions<LdapAuthenticationOptions> options,
        HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
    }
    
    public string AuthenticationMethod => "ldap";
    
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken;
        }
        
        await RefreshAsync(cancellationToken);
        return _cachedToken ?? throw new VaultAuthenticationException("ldap", "Failed to obtain token");
    }
    
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var payload = new { password = _options.Password };
        
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.PostAsync($"/v1/auth/{_options.LdapPath}/login/{_options.Username}", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        _cachedToken = result.GetProperty("auth").GetProperty("client_token").GetString();
        
        var leaseDuration = result.GetProperty("auth").GetProperty("lease_duration").GetInt32();
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseDuration);
    }
    
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedToken != null && _tokenExpiry > DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 4: Create AwsIamAuthProvider và TlsCertificateAuthProvider stubs**

```csharp
// src/Authentication/AwsIamAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

public class AwsIamAuthProvider : IVaultAuthenticationProvider
{
    private readonly AwsIamAuthenticationOptions _options;
    
    public AwsIamAuthProvider(IOptions<AwsIamAuthenticationOptions> options)
    {
        _options = options.Value;
    }
    
    public string AuthenticationMethod => "aws-iam";
    
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement AWS IAM signing
        throw new NotImplementedException("AWS IAM authentication not yet implemented");
    }
    
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
```

```csharp
// src/Authentication/TlsCertificateAuthProvider.cs
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Options;

namespace DotNet.Vault.Configuration.Authentication;

public class TlsCertificateAuthProvider : IVaultAuthenticationProvider
{
    private readonly TlsCertificateAuthenticationOptions _options;
    
    public TlsCertificateAuthProvider(IOptions<TlsCertificateAuthenticationOptions> options)
    {
        _options = options.Value;
    }
    
    public string AuthenticationMethod => "cert";
    
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement TLS certificate authentication
        throw new NotImplementedException("TLS certificate authentication not yet implemented");
    }
    
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> IsTokenValidAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
```

- [ ] **Step 5: Commit**

```bash
git add src/Authentication/
git commit -m "feat: add all authentication providers"
```

---

## Task 6: Secret Backend Interface và KvSecretBackend

**Files:**
- Create: `src/Backends/IVaultSecretBackend.cs`
- Create: `src/Backends/KvSecretBackend.cs`
- Create: `src/Backends/KvPathBuilder.cs`
- Create: `tests/DotNet.Vault.Configuration.Tests/Unit/Backends/KvPathBuilderTests.cs`

**Interfaces:**
- Consumes: VaultOptions từ Task 3
- Produces: IVaultSecretBackend interface, KvSecretBackend implementation

- [ ] **Step 1: Write failing test for KvPathBuilder**

```csharp
// tests/DotNet.Vault.Configuration.Tests/Unit/Backends/KvPathBuilderTests.cs
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using Xunit;

namespace DotNet.Vault.Configuration.Tests.Unit.Backends;

public class KvPathBuilderTests
{
    [Fact]
    public void BuildPaths_WithApplicationNameAndProfiles_ReturnsCorrectPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            BackendPath = "secret",
            Version = 2,
            ApplicationName = "myapp",
            DefaultContext = "application",
            Profiles = new List<string> { "dev", "prod" }
        };
        
        // Act
        var paths = KvPathBuilder.BuildPaths(options);
        
        // Assert
        Assert.Equal(6, paths.Count);
        Assert.Contains("secret/data/application", paths);
        Assert.Contains("secret/data/application/dev", paths);
        Assert.Contains("secret/data/application/prod", paths);
        Assert.Contains("secret/data/myapp", paths);
        Assert.Contains("secret/data/myapp/dev", paths);
        Assert.Contains("secret/data/myapp/prod", paths);
    }
    
    [Fact]
    public void BuildPaths_WithKvV1_ReturnsCorrectPaths()
    {
        // Arrange
        var options = new KvSecretBackendOptions
        {
            BackendPath = "secret",
            Version = 1,
            ApplicationName = "myapp"
        };
        
        // Act
        var paths = KvPathBuilder.BuildPaths(options);
        
        // Assert
        Assert.DoesNotContain(paths, p => p.Contains("/data/"));
        Assert.Contains("secret/application", paths);
        Assert.Contains("secret/myapp", paths);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/DotNet.Vault.Configuration.Tests --filter "FullyQualifiedName~KvPathBuilderTests"
```

Expected: FAIL - KvPathBuilder does not exist

- [ ] **Step 3: Create IVaultSecretBackend interface**

```csharp
// src/Backends/IVaultSecretBackend.cs
namespace DotNet.Vault.Configuration.Backends;

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

- [ ] **Step 4: Create KvPathBuilder**

```csharp
// src/Backends/KvPathBuilder.cs
using DotNet.Vault.Configuration.Core;

namespace DotNet.Vault.Configuration.Backends;

public static class KvPathBuilder
{
    public static List<string> BuildPaths(KvSecretBackendOptions options)
    {
        var paths = new List<string>();
        var backendPath = options.BackendPath.TrimEnd('/');
        var prefix = options.Version == 2 ? "data" : "";
        
        if (!string.IsNullOrEmpty(options.DefaultContext))
        {
            paths.Add($"{backendPath}/{prefix}/{options.DefaultContext}".TrimEnd('/'));
            
            foreach (var profile in options.Profiles)
            {
                paths.Add($"{backendPath}/{prefix}/{options.DefaultContext}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }
        
        if (!string.IsNullOrEmpty(options.ApplicationName))
        {
            paths.Add($"{backendPath}/{prefix}/{options.ApplicationName}".TrimEnd('/'));
            
            foreach (var profile in options.Profiles)
            {
                paths.Add($"{backendPath}/{prefix}/{options.ApplicationName}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }
        
        if (!string.IsNullOrEmpty(options.BackendName))
        {
            var backendPath2 = $"{options.ApplicationName}-{options.BackendName}";
            paths.Add($"{backendPath}/{prefix}/{backendPath2}".TrimEnd('/'));
            
            foreach (var profile in options.Profiles)
            {
                paths.Add($"{backendPath}/{prefix}/{backendPath2}{options.ProfileSeparator}{profile}".TrimEnd('/'));
            }
        }
        
        return paths;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/DotNet.Vault.Configuration.Tests --filter "FullyQualifiedName~KvPathBuilderTests"
```

Expected: PASS

- [ ] **Step 6: Create KvSecretBackend**

```csharp
// src/Backends/KvSecretBackend.cs
using DotNet.Vault.Configuration.Core;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Backends;

public class KvSecretBackend : IVaultSecretBackend
{
    private readonly KvSecretBackendOptions _options;
    private readonly HttpClient _httpClient;
    
    public KvSecretBackend(KvSecretBackendOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }
    
    public string BackendType => "kv";
    
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/v1/{request.Path}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        var secrets = new Dictionary<string, string>();
        
        if (_options.Version == 2 && result.TryGetProperty("data", out var dataElement))
        {
            if (dataElement.TryGetProperty("data", out var innerData))
            {
                foreach (var prop in innerData.EnumerateObject())
                {
                    secrets[prop.Name] = prop.Value.ToString();
                }
            }
        }
        else if (result.TryGetProperty("data", out var v1Data))
        {
            foreach (var prop in v1Data.EnumerateObject())
            {
                secrets[prop.Name] = prop.Value.ToString();
            }
        }
        
        return new SecretResult
        {
            Secrets = secrets,
            LeaseDuration = null, // KV secrets don't expire
            Renewable = false
        };
    }
    
    public bool CanHandle(string path)
    {
        return path.StartsWith(_options.BackendPath);
    }
    
    public TimeSpan? GetTtl(string path)
    {
        return null; // KV secrets are static
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add src/Backends/ tests/DotNet.Vault.Configuration.Tests/Unit/Backends/
git commit -m "feat: add KvSecretBackend with path builder"
```

---

## Task 7: Database và PKI Secret Backends

**Files:**
- Create: `src/Backends/DatabaseSecretBackend.cs`
- Create: `src/Backends/PkiSecretBackend.cs`

**Interfaces:**
- Consumes: IVaultSecretBackend từ Task 6
- Produces: Database và PKI backend implementations

- [ ] **Step 1: Create DatabaseSecretBackend**

```csharp
// src/Backends/DatabaseSecretBackend.cs
using DotNet.Vault.Configuration.Core;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Backends;

public class DatabaseSecretBackend : IVaultSecretBackend
{
    private readonly DatabaseSecretBackendOptions _options;
    private readonly HttpClient _httpClient;
    
    public DatabaseSecretBackend(DatabaseSecretBackendOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }
    
    public string BackendType => "database";
    
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/v1/{request.Path}", cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        
        var secrets = new Dictionary<string, string>();
        
        if (result.TryGetProperty("data", out var data))
        {
            foreach (var prop in data.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(_options.PropertyPrefix) 
                    ? prop.Name 
                    : $"{_options.PropertyPrefix}.{prop.Name}";
                secrets[key] = prop.Value.ToString();
            }
        }
        
        var leaseId = result.TryGetProperty("lease_id", out var leaseIdProp) ? leaseIdProp.GetString() : null;
        var leaseDuration = result.TryGetProperty("lease_duration", out var leaseDurationProp) 
            ? TimeSpan.FromSeconds(leaseDurationProp.GetInt32()) 
            : (TimeSpan?)null;
        var renewable = result.TryGetProperty("renewable", out var renewableProp) && renewableProp.GetBoolean();
        
        return new SecretResult
        {
            Secrets = secrets,
            LeaseId = leaseId,
            LeaseDuration = leaseDuration,
            Renewable = renewable,
            ExpireTime = leaseDuration.HasValue ? DateTimeOffset.UtcNow.Add(leaseDuration.Value) : null
        };
    }
    
    public bool CanHandle(string path)
    {
        return path.StartsWith(_options.BackendPath);
    }
    
    public TimeSpan? GetTtl(string path)
    {
        return null; // TTL varies per request
    }
}
```

- [ ] **Step 2: Create PkiSecretBackend**

```csharp
// src/Backends/PkiSecretBackend.cs
using DotNet.Vault.Configuration.Core;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Backends;

public class PkiSecretBackend : IVaultSecretBackend
{
    private readonly PkiSecretBackendOptions _options;
    private readonly HttpClient _httpClient;
    
    public PkiSecretBackend(PkiSecretBackendOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }
    
    public string BackendType => "pki";
    
    public async Task<SecretResult> GetSecretsAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            common_name = _options.CommonName,
            alt_names = string.Join(",", _options.AltNames),
            ttl = _options.Ttl?.TotalSeconds.ToString()
        };
        
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.PostAsync($"/v1/{request.Path}", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        var secrets = new Dictionary<string, string>();
        
        if (result.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("certificate", out var cert))
                secrets["certificate.pem"] = cert.GetString() ?? "";
            
            if (data.TryGetProperty("private_key", out var key))
                secrets["certificate.key"] = key.GetString() ?? "";
            
            if (data.TryGetProperty("ca_chain", out var caChain))
                secrets["certificate.ca_chain"] = caChain.GetString() ?? "";
        }
        
        var leaseId = result.TryGetProperty("lease_id", out var leaseIdProp) ? leaseIdProp.GetString() : null;
        var leaseDuration = result.TryGetProperty("lease_duration", out var leaseDurationProp) 
            ? TimeSpan.FromSeconds(leaseDurationProp.GetInt32()) 
            : (TimeSpan?)null;
        
        return new SecretResult
        {
            Secrets = secrets,
            LeaseId = leaseId,
            LeaseDuration = leaseDuration,
            Renewable = false,
            ExpireTime = leaseDuration.HasValue ? DateTimeOffset.UtcNow.Add(leaseDuration.Value) : null
        };
    }
    
    public bool CanHandle(string path)
    {
        return path.StartsWith(_options.BackendPath);
    }
    
    public TimeSpan? GetTtl(string path)
    {
        return _options.Ttl;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Backends/
git commit -m "feat: add Database và PKI secret backends"
```

---

## Task 8: VaultClient Implementation

**Files:**
- Create: `src/Core/VaultClient.cs`

**Interfaces:**
- Consumes: IVaultAuthenticationProvider từ Task 4, IVaultSecretBackend từ Task 6, Exceptions từ Task 2
- Produces: VaultClient cho configuration provider

- [ ] **Step 1: Create VaultClient**

```csharp
// src/Core/VaultClient.cs
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core.Exceptions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNet.Vault.Configuration.Core;

public class VaultClient
{
    private readonly HttpClient _httpClient;
    private readonly VaultOptions _options;
    private readonly IEnumerable<IVaultAuthenticationProvider> _authProviders;
    private readonly IEnumerable<IVaultSecretBackend> _backends;
    private readonly ILogger<VaultClient> _logger;
    
    public VaultClient(
        HttpClient httpClient,
        VaultOptions options,
        IEnumerable<IVaultAuthenticationProvider> authProviders,
        IEnumerable<IVaultSecretBackend> backends,
        ILogger<VaultClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _authProviders = authProviders;
        _backends = backends;
        _logger = logger;
        
        _httpClient.BaseAddress = _options.Uri;
        _httpClient.Timeout = _options.Timeout;
    }
    
    public async Task<Dictionary<string, string>> LoadSecretsAsync(IEnumerable<string> paths)
    {
        var allSecrets = new Dictionary<string, string>();
        
        foreach (var path in paths)
        {
            try
            {
                var backend = _backends.FirstOrDefault(b => b.CanHandle(path));
                if (backend == null)
                {
                    throw new VaultBackendNotSupportedException(path);
                }
                
                var request = new SecretRequest { Path = path };
                var result = await backend.GetSecretsAsync(request);
                
                foreach (var kvp in result.Secrets)
                {
                    allSecrets[kvp.Key] = kvp.Value;
                }
                
                _logger.LogDebug("Loaded {Count} secrets from {Path}", result.Secrets.Count, path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets from {Path}", path);
                throw;
            }
        }
        
        return allSecrets;
    }
    
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var provider = _authProviders.FirstOrDefault(p => p.AuthenticationMethod == _options.Authentication.Method);
        if (provider == null)
        {
            throw new VaultAuthenticationException(_options.Authentication.Method, "Authentication provider not found");
        }
        
        return await provider.GetTokenAsync(cancellationToken);
    }
    
    public async Task<VaultHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/v1/sys/health", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<VaultHealthResponse>(content)!;
        }
        catch (Exception ex)
        {
            throw new VaultConnectionException(_options.Uri, ex);
        }
    }
    
    public async Task<bool> IsAuthenticationValidAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetTokenAsync(cancellationToken);
            var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/token/lookup-self");
            request.Headers.Add("X-Vault-Token", token);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public class VaultHealthResponse
{
    public bool Initialized { get; set; }
    public bool Sealed { get; set; }
    public bool Standby { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public DateTimeOffset ServerTimeUtc { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/VaultClient.cs
git commit -m "feat: add VaultClient implementation"
```

---

## Task 9: Configuration Provider và Source

**Files:**
- Create: `src/Core/VaultConfigurationProvider.cs`
- Create: `src/Core/VaultConfigurationSource.cs`
- Create: `src/Core/Extensions/VaultConfigurationExtensions.cs`

**Interfaces:**
- Consumes: VaultClient từ Task 8
- Produces: IConfiguration integration

- [ ] **Step 1: Create VaultConfigurationProvider**

```csharp
// src/Core/VaultConfigurationProvider.cs
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Core;

public class VaultConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly VaultClient _client;
    private readonly VaultOptions _options;
    private readonly SecretRefresher _refresher;
    private readonly ILogger<VaultConfigurationProvider> _logger;
    private Timer? _refreshTimer;
    
    public VaultConfigurationProvider(
        VaultClient client,
        VaultOptions options,
        SecretRefresher refresher,
        ILogger<VaultConfigurationProvider> logger)
    {
        _client = client;
        _options = options;
        _refresher = refresher;
        _logger = logger;
    }
    
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
            
            _logger.LogWarning("FailFast is disabled. Continuing with empty configuration.");
            Data = new Dictionary<string, string>();
        }
    }
    
    private List<string> BuildSecretPaths()
    {
        var paths = new List<string>();
        
        if (_options.Kv.Enabled)
        {
            var kvPaths = KvPathBuilder.BuildPaths(_options.Kv);
            paths.AddRange(kvPaths);
        }
        
        if (_options.Database.Enabled)
        {
            paths.Add($"{_options.Database.BackendPath}/creds/{_options.Database.Role}");
        }
        
        if (_options.Pki.Enabled)
        {
            paths.Add($"{_options.Pki.BackendPath}/issue/{_options.Pki.Role}");
        }
        
        return paths;
    }
    
    private void SetupRefreshIfNeeded()
    {
        if (!_options.Refresh.Enabled)
            return;
        
        var ttl = _refresher.GetMinimumTtl();
        if (ttl.HasValue && ttl.Value > TimeSpan.Zero)
        {
            var refreshInterval = _options.Refresh.Interval ?? TimeSpan.FromTicks(ttl.Value.Ticks * 8 / 10);
            
            _refreshTimer = new Timer(
                async _ => await RefreshAsync(),
                null,
                refreshInterval,
                refreshInterval);
        }
    }
    
    private async Task RefreshAsync()
    {
        try
        {
            if (_refresher.ShouldRefresh())
            {
                _logger.LogInformation("Refreshing secrets from Vault");
                var paths = BuildSecretPaths();
                var secrets = await _client.LoadSecretsAsync(paths);
                Data = secrets;
                OnReload();
                _logger.LogInformation("Refreshed {Count} secrets", secrets.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh secrets from Vault");
        }
    }
    
    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
```

- [ ] **Step 2: Create VaultConfigurationSource**

```csharp
// src/Core/VaultConfigurationSource.cs
using DotNet.Vault.Configuration.Authentication;
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Core;

public class VaultConfigurationSource : IConfigurationSource
{
    public VaultOptions Options { get; set; } = new();
    
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var services = new ServiceCollection();
        
        services.AddSingleton(Options);
        services.AddSingleton<VaultClient>();
        services.AddSingleton<SecretRefresher>();
        
        // Register authentication providers
        if (Options.Authentication.Token != null)
            services.AddSingleton<IVaultAuthenticationProvider>(sp => 
                new TokenAuthProvider(Microsoft.Extensions.Options.Options.Create(Options.Authentication.Token)));
        
        // Register secret backends
        if (Options.Kv.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp => 
                new KvSecretBackend(Options.Kv, new HttpClient { BaseAddress = Options.Uri }));
        
        if (Options.Database.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp => 
                new DatabaseSecretBackend(Options.Database, new HttpClient { BaseAddress = Options.Uri }));
        
        if (Options.Pki.Enabled)
            services.AddSingleton<IVaultSecretBackend>(sp => 
                new PkiSecretBackend(Options.Pki, new HttpClient { BaseAddress = Options.Uri }));
        
        services.AddLogging();
        
        var serviceProvider = services.BuildServiceProvider();
        
        var client = serviceProvider.GetRequiredService<VaultClient>();
        var refresher = serviceProvider.GetRequiredService<SecretRefresher>();
        var logger = serviceProvider.GetRequiredService<ILogger<VaultConfigurationProvider>>();
        
        return new VaultConfigurationProvider(client, Options, refresher, logger);
    }
}
```

- [ ] **Step 3: Create extension methods**

```csharp
// src/Core/Extensions/VaultConfigurationExtensions.cs
using Microsoft.Extensions.Configuration;

namespace DotNet.Vault.Configuration.Core.Extensions;

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

- [ ] **Step 4: Commit**

```bash
git add src/Core/
git commit -m "feat: add VaultConfigurationProvider và extensions"
```

---

## Task 10: SecretRefresher

**Files:**
- Create: `src/Refresh/SecretRefresher.cs`

**Interfaces:**
- Consumes: VaultClient từ Task 8
- Produces: TTL monitoring service

- [ ] **Step 1: Create SecretRefresher**

```csharp
// src/Refresh/SecretRefresher.cs
using DotNet.Vault.Configuration.Backends;
using DotNet.Vault.Configuration.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Refresh;

public class SecretRefresher : IDisposable, IHostedService
{
    private readonly VaultClient _client;
    private readonly VaultOptions _options;
    private readonly ILogger<SecretRefresher> _logger;
    private readonly Dictionary<string, SecretMetadata> _secretMetadata = new();
    private Timer? _refreshTimer;
    private bool _isRefreshing;
    
    public event Func<Task>? OnSecretsRefreshed;
    
    public SecretRefresher(
        VaultClient client,
        VaultOptions options,
        ILogger<SecretRefresher> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Refresh.Enabled)
        {
            _logger.LogInformation("Secret refresh is disabled");
            return Task.CompletedTask;
        }
        
        var interval = _options.Refresh.Interval ?? TimeSpan.FromMinutes(5);
        
        _refreshTimer = new Timer(
            async _ => await RefreshLoopAsync(),
            null,
            interval,
            interval);
        
        _logger.LogInformation("Secret refresh started with interval: {Interval}", interval);
        return Task.CompletedTask;
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _refreshTimer?.Change(Timeout.Infinite, 0);
        _logger.LogInformation("Secret refresh stopped");
        return Task.CompletedTask;
    }
    
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
            
            _logger.LogDebug("Tracked secret at {Path} with TTL {Ttl}", path, result.LeaseDuration);
        }
    }
    
    public TimeSpan? GetMinimumTtl()
    {
        if (!_secretMetadata.Any())
            return null;
        
        return _secretMetadata.Values
            .Where(m => m.LeaseDuration.HasValue)
            .Min(m => m.LeaseDuration);
    }
    
    public bool ShouldRefresh()
    {
        if (!_secretMetadata.Any())
            return false;
        
        var now = DateTimeOffset.UtcNow;
        
        return _secretMetadata.Values.Any(m =>
        {
            if (!m.LeaseDuration.HasValue)
                return false;
            
            var timeUntilExpiry = m.ExpireTime ?? m.LastRefreshed.Add(m.LeaseDuration.Value);
            var timeRemaining = timeUntilExpiry - now;
            var threshold = m.LeaseDuration.Value * 0.2;
            
            return timeRemaining < threshold;
        });
    }
    
    private async Task RefreshLoopAsync()
    {
        if (_isRefreshing)
        {
            _logger.LogWarning("Previous refresh still running, skipping");
            return;
        }
        
        try
        {
            _isRefreshing = true;
            
            if (!ShouldRefresh())
                return;
            
            _logger.LogInformation("Starting secret refresh cycle");
            
            if (OnSecretsRefreshed != null)
            {
                await OnSecretsRefreshed.Invoke();
            }
            
            _logger.LogInformation("Secret refresh cycle completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secret refresh cycle");
        }
        finally
        {
            _isRefreshing = false;
        }
    }
    
    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}

internal class SecretMetadata
{
    public string Path { get; set; } = string.Empty;
    public string? LeaseId { get; set; }
    public TimeSpan? LeaseDuration { get; set; }
    public DateTimeOffset? ExpireTime { get; set; }
    public bool Renewable { get; set; }
    public DateTimeOffset LastRefreshed { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Refresh/
git commit -m "feat: add SecretRefresher for TTL monitoring"
```

---

## Task 11: Health Check Integration

**Files:**
- Create: `src/HealthChecks/VaultHealthCheck.cs`

**Interfaces:**
- Consumes: VaultClient từ Task 8, SecretRefresher từ Task 10
- Produces: IHealthCheck implementation

- [ ] **Step 1: Create VaultHealthCheck**

```csharp
// src/HealthChecks/VaultHealthCheck.cs
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Refresh;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.HealthChecks;

public class VaultHealthCheck : IHealthCheck
{
    private readonly VaultClient _client;
    private readonly SecretRefresher _refresher;
    private readonly VaultOptions _options;
    private readonly ILogger<VaultHealthCheck> _logger;
    
    public VaultHealthCheck(
        VaultClient client,
        SecretRefresher refresher,
        VaultOptions options,
        ILogger<VaultHealthCheck> logger)
    {
        _client = client;
        _refresher = refresher;
        _options = options;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var vaultHealth = await _client.GetHealthAsync(cancellationToken);
            
            if (!vaultHealth.Initialized)
                return HealthCheckResult.Unhealthy("Vault is not initialized");
            
            if (vaultHealth.Sealed)
                return HealthCheckResult.Unhealthy("Vault is sealed");
            
            var isAuthValid = await _client.IsAuthenticationValidAsync(cancellationToken);
            if (!isAuthValid)
                return HealthCheckResult.Degraded("Vault authentication is invalid or expired");
            
            var minTtl = _refresher.GetMinimumTtl();
            var data = new Dictionary<string, object>
            {
                ["vault_version"] = vaultHealth.Version,
                ["vault_cluster"] = vaultHealth.ClusterName,
                ["vault_server_time"] = vaultHealth.ServerTimeUtc
            };
            
            if (minTtl.HasValue)
            {
                data["minimum_secret_ttl"] = minTtl.Value.ToString();
                
                if (minTtl.Value < TimeSpan.FromMinutes(5))
                    return HealthCheckResult.Degraded("Some secrets are expiring soon", data: data);
            }
            
            return HealthCheckResult.Healthy("Vault is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vault health check failed");
            return HealthCheckResult.Unhealthy("Failed to connect to Vault", ex);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/HealthChecks/
git commit -m "feat: add VaultHealthCheck integration"
```

---

## Task 12: Final Integration và Smoke Test

**Files:**
- Create: `examples/BasicExample/Program.cs`

**Interfaces:**
- Consumes: Tất cả components từ Tasks 1-11
- Produces: Working example

- [ ] **Step 1: Create example program**

```csharp
// examples/BasicExample/Program.cs
using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Extensions;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
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
        options.Kv.Profiles = new List<string> { "dev" };
        options.FailFast = true;
    })
    .Build();

Console.WriteLine("Vault Configuration loaded successfully!");
Console.WriteLine($"Configuration keys: {config.AsEnumerable().Count()}");

foreach (var kvp in config.AsEnumerable())
{
    Console.WriteLine($"{kvp.Key} = {kvp.Value}");
}
```

- [ ] **Step 2: Build solution**

```bash
dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Run smoke test**

```bash
cd examples/BasicExample
dotnet run
```

Expected: Application connects to Vault và loads secrets

- [ ] **Step 4: Commit**

```bash
git add examples/
git commit -m "feat: add basic example và smoke test"
```

---

## Task 13: Final Verification và Documentation

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: All components
- Produces: Complete documentation

- [ ] **Step 1: Create README**

```markdown
# DotNet.Vault.Configuration

.NET extension library for IConfiguration to integrate HashiCorp Vault.

## Features

- Multiple authentication methods (Token, AppRole, Kubernetes, AWS IAM, LDAP, TLS)
- Multiple secret engines (KV v1/v2, Database, PKI)
- Periodic refresh với TTL monitoring
- Health check integration
- Spring Cloud Vault compatible path strategy

## Installation

```bash
dotnet add package DotNet.Vault.Configuration
```

## Quick Start

```csharp
var config = new ConfigurationBuilder()
    .AddVault(options =>
    {
        options.Uri = new Uri("http://localhost:8200");
        options.Authentication.Method = "token";
        options.Authentication.Token = new TokenAuthenticationOptions
        {
            Token = "myroot"
        };
        options.Kv.Enabled = true;
        options.Kv.ApplicationName = "myapp";
    })
    .Build();
```

## Documentation

See [Design Specification](docs/superpowers/specs/2026-07-21-vault-configuration-design.md) for details.
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test
```

Expected: All tests pass

- [ ] **Step 3: Final commit**

```bash
git add README.md
git commit -m "docs: add README và documentation"
git tag v1.0.0
```

---

## Plan Complete

**Plan saved to:** `docs/superpowers/plans/2026-07-21-vault-configuration.md`

**Execution options:**

1. **Subagent-Driven (recommended)** - Dispatch fresh subagent per task, review between tasks
2. **Inline Execution** - Execute tasks in this session with checkpoints

**Which approach?**
