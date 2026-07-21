# DotNet.Vault.Configuration

.NET extension library for `IConfiguration` to integrate HashiCorp Vault, inspired by Spring Cloud Vault.

## Features

- **Multiple authentication methods**: Token, AppRole, Kubernetes, LDAP (full implementations), AWS IAM, TLS Certificate (stubs for future implementation)
- **Multiple secret engines**: KV v1/v2, Database, PKI
- **Periodic refresh** with TTL monitoring for dynamic credentials
- **Health check integration** with `Microsoft.Extensions.Diagnostics.HealthChecks`
- **Spring Cloud Vault compatible path strategy** (application name + profiles)
- **Fail-fast mode** with configurable fallback
- **Structured logging** via `ILogger`

## Installation

```bash
dotnet add package DotNet.Vault.Configuration
```

(Available after the package is published to NuGet.)

## Quick Start

```csharp
using DotNet.Vault.Configuration.Core.Extensions;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddVault(options =>
    {
        options.Uri = new Uri("http://localhost:8200");
        options.Authentication.Method = "token";
        options.Authentication.Token = new TokenAuthenticationOptions
        {
            Token = Environment.GetEnvironmentVariable("VAULT_TOKEN")
        };
        options.Kv.Enabled = true;
        options.Kv.BackendPath = "secret";
        options.Kv.Version = 2;
        options.Kv.ApplicationName = "myapp";
        options.FailFast = true;
    })
    .Build();

var connectionString = config["ConnectionStrings:DefaultConnection"];
```

## Advanced Configuration

```csharp
builder.Configuration.AddVault(options =>
{
    // Vault connection
    options.Uri = new Uri("https://vault.example.com");
    options.Namespace = "my-namespace"; // Enterprise only
    options.Timeout = TimeSpan.FromSeconds(30);
    
    // Authentication
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
    
    // Database backend (dynamic credentials)
    options.Database.Enabled = true;
    options.Database.BackendPath = "database";
    options.Database.Role = "myapp-role";
    options.Database.PropertyPrefix = "spring.datasource";
    
    // PKI backend (certificates)
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

## Health Check

```csharp
builder.Services.AddHealthChecks()
    .AddVault(name: "vault", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready", "vault" });
```

## Authentication Methods

| Method | Status | Use Case |
|--------|--------|----------|
| Token | ✅ Full | Dev/testing, external token management |
| AppRole | ✅ Full | Machine-to-machine, services |
| Kubernetes | ✅ Full | Applications in K8s clusters |
| LDAP | ✅ Full | Enterprise LDAP/AD |
| AWS IAM | 🚧 Stub | EC2/ECS/Lambda (requires AWS SDK) |
| TLS Certificate | 🚧 Stub | High-security environments |

## Secret Engines

| Engine | Status | Description |
|--------|--------|-------------|
| KV v1 | ✅ Full | Key-value v1 (no versioning) |
| KV v2 | ✅ Full | Key-value v2 (with versioning) |
| Database | ✅ Full | Dynamic database credentials |
| PKI | ✅ Full | X.509 certificate generation |

## Path Strategy

The library uses Spring Cloud Vault's path strategy: secrets are loaded in priority order so later (more specific) paths override earlier ones.

With `ApplicationName = "myapp"`, `Profiles = ["dev", "prod"]`, `BackendPath = "secret"`:

```
1. secret/data/application           # Default context
2. secret/data/application/dev       # Default context + profile
3. secret/data/application/prod
4. secret/data/myapp                 # Application name
5. secret/data/myapp/dev             # Application name + profile
6. secret/data/myapp/prod            # (highest priority)
```

KV v1 paths are the same without the `data/` prefix.

## Documentation

- [Design Specification](docs/superpowers/specs/2026-07-21-vault-configuration-design.md)
- [Implementation Plan](docs/superpowers/plans/2026-07-21-vault-configuration.md)

## Testing

```bash
# Run unit tests
dotnet test

# Run smoke test against Vault dev server
cd examples/BasicExample
dotnet run
```

## License

MIT (or your preferred license)
