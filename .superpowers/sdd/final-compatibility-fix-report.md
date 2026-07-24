# Final Compatibility Fix Report

## Status

Complete. The v1.0.0 public constructor surface removed by the v1.0.1 factory/refresh work has been restored while retaining the factory-based constructors.

## Constructor Coverage

- `VaultClient(HttpClient, VaultOptions, IEnumerable<IVaultAuthenticationProvider>, IEnumerable<IVaultSecretBackend>, ILogger<VaultClient>)`
  - Configures the caller-owned client with the v1.0.0 base address and timeout, then forwards through the current factory-based path.
- `AppRoleAuthProvider(IOptions<AppRoleAuthenticationOptions>, HttpClient)`
- `KubernetesAuthProvider(IOptions<KubernetesAuthenticationOptions>, HttpClient)`
- `LdapAuthProvider(IOptions<LdapAuthenticationOptions>, HttpClient)`
  - Each forwards through the current named-auth-client path without disposing the caller-owned client. Login requests remain isolated to `vault-auth-client` for factory registrations.
- `KvSecretBackend(KvSecretBackendOptions, HttpClient, IVaultAuthenticationProvider?)`
- `DatabaseSecretBackend(DatabaseSecretBackendOptions, HttpClient, IVaultAuthenticationProvider?)`
- `PkiSecretBackend(PkiSecretBackendOptions, HttpClient, IVaultAuthenticationProvider?)`
  - Each forwards HTTP calls through the factory-based implementation. Legacy construction intentionally has no refresher, matching v1.0.0 lease-tracking behavior.
- `SecretRefresher(VaultClient, VaultOptions, ILogger<SecretRefresher>)`
  - Uses the current scheduler and lease-renewal implementation. Renewable leases obtain the token from the legacy `VaultClient` and attach it to renewal requests.
- `VaultConfigurationProvider(VaultClient, VaultOptions, SecretRefresher, ILogger<VaultConfigurationProvider>)`
  - Restored as a real four-parameter overload; it forwards to the current service-provider-aware constructor.

## Implementation Notes

- Added internal `SingleHttpClientFactory`, which returns a supplied caller-owned `HttpClient` without transferring disposal ownership.
- `VaultConfigurationSource` explicitly registers the factory-based `VaultClient` and `SecretRefresher` constructors, preventing public overload ambiguity in the library-owned DI container.
- Auth providers no longer dispose a client returned by their factory; this is required for legacy caller-owned clients and remains compatible with `IHttpClientFactory` lifetime management.

## Tests

- `PublicConstructorCompatibilityTests` covers all restored direct-client constructor call sites, VaultClient base address/timeout behavior, backend requests without a refresher, auth-client reuse after login, the legacy SecretRefresher lifecycle, authenticated legacy lease renewal, and the exact four-parameter provider overload.
- Focused compatibility test command:
  `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PublicConstructorCompatibilityTests`
  - Passed: 6 tests.
- Full Release test project:
  `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --configuration Release --no-restore`
  - Passed: 121 tests, 0 failed, 0 skipped.
- Release solution build:
  `dotnet build DotNet.Vault.Configuration.slnx --configuration Release --no-restore`
  - Succeeded.
- BasicExample smoke:
  `dotnet run --project examples/BasicExample/BasicExample.csproj --configuration Release --no-build`
  - Passed; it loaded zero configuration keys under the local environment's unavailable Vault configuration.

## Concerns

- A consumer that manually registers both a bare `HttpClient` and `VaultClient` by implementation type in a custom DI container must explicitly register the factory-based `VaultClient` constructor to avoid equal-arity constructor selection ambiguity. The library-owned `VaultConfigurationSource` does this explicitly.
- The legacy backend overloads preserve v1.0.0 behavior by not tracking leases because their original signatures have no `SecretRefresher` parameter.
