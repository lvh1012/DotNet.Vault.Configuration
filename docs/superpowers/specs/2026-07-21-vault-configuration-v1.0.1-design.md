# DotNet.Vault.Configuration v1.0.1 Hardening Design Specification

**Date:** 2026-07-21  
**Status:** Approved  
**Base Version:** v1.0.0 (commit 3cd1732)  
**Target Version:** v1.0.1

## Overview

v1.0.1 addresses all critical and important issues identified in the v1.0.0 final review, plus minor cleanups. Scope includes infrastructure refactor (HttpClient factory + Polly), refresh pipeline improvements (lease renewal, event-driven refresh), thread-safety fixes, SSL configuration, comprehensive test coverage, and code cleanup.

## Phased Approach

6 sequential phases, each builds on the previous:

| Phase | Focus | Issues |
|-------|-------|--------|
| **A: Foundation** | IHttpClientFactory + DelegatingHandler + Polly | I7, I6 (use), M7 |
| **B: Refresh Pipeline** | TrackSecret, OnSecretsRefreshed, LeaseRenewer | I3, I4, I2, M8 |
| **C: Reliability** | SemaphoreSlim in auth providers | I5 |
| **D: Features** | VaultSslOptions | I1 |
| **E: Test Coverage** | 5% → 80%+ | I8 |
| **F: Cleanup** | Warnings, dead code, malformed docs | M1, M2, M3, M4, M5 |

## Phase A: Foundation (HttpClient + Polly + DelegatingHandler)

### Goals
- Consolidate 4 HttpClient instances → 1 named client via `IHttpClientFactory`
- Add automatic X-Vault-Token attachment via `DelegatingHandler`
- Wire Polly retry policy using existing `VaultRetryOptions`
- Clean up `BaseAddress` double-set bug (M7)

### New Files

#### `src/Http/VaultAuthDelegatingHandler.cs`

```csharp
using DotNet.Vault.Configuration.Authentication;
using Microsoft.Extensions.Logging;

namespace DotNet.Vault.Configuration.Http;

public class VaultAuthDelegatingHandler : DelegatingHandler
{
    private readonly IVaultAuthenticationProvider _authProvider;
    private readonly ILogger<VaultAuthDelegatingHandler> _logger;
    
    public VaultAuthDelegatingHandler(
        IVaultAuthenticationProvider authProvider,
        ILogger<VaultAuthDelegatingHandler> logger)
    {
        _authProvider = authProvider;
        _logger = logger;
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains("X-Vault-Token"))
        {
            try
            {
                var token = await _authProvider.GetTokenAsync(cancellationToken);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Add("X-Vault-Token", token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to attach X-Vault-Token; request may fail");
            }
        }
        
        return await base.SendAsync(request, cancellationToken);
    }
}
```

#### `src/Http/VaultHttpClientFactoryExtensions.cs`

```csharp
public static class VaultHttpClientFactoryExtensions
{
    public const string VaultClientName = "vault-client";
    
    public static IHttpClientBuilder AddVaultHttpClient(
        this IServiceCollection services,
        VaultOptions options)
    {
        return services.AddHttpClient(VaultClientName, client =>
        {
            client.BaseAddress = options.Uri;
            client.Timeout = options.Timeout;
        })
        .AddHttpMessageHandler<VaultAuthDelegatingHandler>()
        .AddPolicyHandler((sp, request) =>
        {
            var logger = sp.GetRequiredService<ILogger<VaultClient>>();
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => (int)msg.StatusCode == 412)
                .WaitAndRetryAsync(
                    retryCount: options.Refresh.Retry.MaxRetries,
                    sleepDurationProvider: attempt =>
                    {
                        var delay = TimeSpan.FromTicks(
                            (long)(options.Refresh.Retry.InitialInterval.Ticks *
                            Math.Pow(options.Refresh.Retry.Multiplier, attempt - 1)));
                        return delay > options.Refresh.Retry.MaxInterval
                            ? options.Refresh.Retry.MaxInterval
                            : delay;
                    },
                    onRetry: (outcome, delay, attempt, _) =>
                    {
                        logger.LogWarning(
                            "Vault request failed (attempt {Attempt}/{Max}). Retrying in {Delay}",
                            attempt, options.Refresh.Retry.MaxRetries, delay);
                    });
        });
    }
}
```

### Modified Files

- `src/Core/VaultConfigurationSource.cs` - use `IHttpClientFactory`, inject named client into auth providers and backends
- `src/Core/VaultClient.cs` - replace `HttpClient` with `IHttpClientFactory`, remove duplicate BaseAddress/Timeout
- `src/Backends/{Kv,Database,Pki}SecretBackend.cs` - replace `HttpClient` with `IHttpClientFactory`

### Dependencies Added

```xml
<PackageReference Include="Polly" Version="8.5.0" />
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.0.0" />
```

## Phase B: Refresh Pipeline (I3 + I4 + I2)

### Goals
- All backends call `SecretRefresher.TrackSecret()` when returning leases
- `VaultConfigurationProvider` subscribes to `OnSecretsRefreshed` event, removes its own duplicate timer
- Add lease renewal via `POST /v1/sys/leases/renew`
- Fix PKI `ttl` JSON key (M8)

### New File: `src/Refresh/VaultLeaseRenewer.cs`

```csharp
public class VaultLeaseRenewer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VaultLeaseRenewer> _logger;
    
    public VaultLeaseRenewer(
        IHttpClientFactory httpClientFactory,
        ILogger<VaultLeaseRenewer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    public async Task<TimeSpan?> RenewAsync(
        string leaseId,
        TimeSpan increment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(VaultHttpClientFactoryExtensions.VaultClientName);
            var payload = new { lease_id = leaseId, increment = (int)increment.TotalSeconds };
            var response = await client.PutAsJsonAsync("/v1/sys/leases/renew", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return result.TryGetProperty("lease_duration", out var d) 
                ? TimeSpan.FromSeconds(d.GetInt32()) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to renew lease {LeaseId}", leaseId);
            throw new VaultLeaseRenewalException(leaseId, ex.Message);
        }
    }
}
```

### Modified Files

- `src/Refresh/SecretRefresher.cs` - add `VaultLeaseRenewer` dependency, implement `RenewLeasesAsync`, use `ConcurrentDictionary`, use `Interlocked` for `_isRefreshing`
- `src/Core/VaultConfigurationProvider.cs` - subscribe to `OnSecretsRefreshed`, remove own `Timer`, add `HandleSecretsRefreshed` method
- `src/Backends/{Kv,Database,Pki}SecretBackend.cs` - inject `SecretRefresher`, call `TrackSecret` in `GetSecretsAsync`
- `src/Backends/PkiSecretBackend.cs` - only include `ttl` JSON key when `_options.Ttl.HasValue` (M8)

## Phase C: Reliability (I5)

### Goals
- Fix race condition in `AppRoleAuthProvider`, `KubernetesAuthProvider`, `LdapAuthProvider`
- Use `SemaphoreSlim` to serialize refresh calls
- Use `Volatile.Read/Write` for field access

### Pattern Applied to 3 Auth Providers

```csharp
private readonly SemaphoreSlim _refreshLock = new(1, 1);
private string? _cachedToken;
private DateTimeOffset? _tokenExpiry;

public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
{
    var token = Volatile.Read(ref _cachedToken);
    var expiry = Volatile.Read(ref _tokenExpiry);
    if (token != null && expiry.HasValue && expiry > DateTimeOffset.UtcNow.AddMinutes(5))
        return token;
    
    await _refreshLock.WaitAsync(cancellationToken);
    try
    {
        // double-check after lock
        token = Volatile.Read(ref _cachedToken);
        expiry = Volatile.Read(ref _tokenExpiry);
        if (token != null && expiry.HasValue && expiry > DateTimeOffset.UtcNow.AddMinutes(5))
            return token;
        
        await RefreshInternalAsync(cancellationToken);
        return Volatile.Read(ref _cachedToken) 
            ?? throw new VaultAuthenticationException("approle", "Failed to obtain token");
    }
    finally
    {
        _refreshLock.Release();
    }
}
```

Also: replace `HttpClient` parameter with `IHttpClientFactory` in all 3 providers.

## Phase D: Features (I1)

### Goals
- Add `VaultSslOptions` class for SSL/TLS configuration
- Support: custom CA, client certificate (mTLS), protocol selection, revocation check
- Backward compatible defaults

### New File: `src/Security/VaultSslOptions.cs`

```csharp
public class VaultSslOptions
{
    public string? CaCertificatePath { get; set; }
    public string? ClientCertificatePath { get; set; }
    public string? ClientCertificatePassword { get; set; }
    public X509Certificate2? CaCertificate { get; set; }
    public X509Certificate2? ClientCertificate { get; set; }
    public bool SkipVerify { get; set; } = false;
    public SslProtocols Protocol { get; set; } = SslProtocols.Tls12;
    public bool CheckCertificateRevocation { get; set; } = true;
    public string? ServerName { get; set; }
}
```

### Modified Files

- `src/Core/VaultOptions.cs` - add `Ssl` property
- `src/Http/VaultHttpClientFactoryExtensions.cs` - add `CreatePrimaryHandler(options)` for SSL config, configure `SocketsHttpHandler.SslOptions`

## Phase E: Test Coverage (I8)

### Current State
- 5 tests (~5% coverage)
- Only TokenAuthProvider và KvPathBuilder

### Target
- ~100 new tests across 16 test files
- 80%+ line coverage
- All public types và methods covered
- All critical paths have negative tests

### Test Files to Create/Expand

| Test File | Tests | Notes |
|-----------|-------|-------|
| `Unit/Authentication/AppRoleAuthProviderTests.cs` | 8 | NEW |
| `Unit/Authentication/KubernetesAuthProviderTests.cs` | 8 | NEW |
| `Unit/Authentication/LdapAuthProviderTests.cs` | 8 | NEW |
| `Unit/Core/VaultClientTests.cs` | 13 | NEW (includes M5 health check tests) |
| `Unit/Core/VaultConfigurationProviderTests.cs` | 7 | NEW |
| `Unit/Core/VaultConfigurationSourceTests.cs` | 3 | NEW |
| `Unit/Core/Exceptions/ExceptionTests.cs` | 8 | NEW |
| `Unit/Backends/KvSecretBackendTests.cs` | 5 | NEW |
| `Unit/Backends/DatabaseSecretBackendTests.cs` | 3 | NEW |
| `Unit/Backends/PkiSecretBackendTests.cs` | 3 | NEW |
| `Unit/Backends/KvPathBuilderTests.cs` | +8 | EXPAND from 2 to 10 |
| `Unit/Refresh/SecretRefresherTests.cs` | 11 | NEW |
| `Unit/Refresh/VaultLeaseRenewerTests.cs` | 3 | NEW |
| `Unit/HealthChecks/VaultHealthCheckTests.cs` | 7 | NEW |
| `Unit/Http/VaultAuthDelegatingHandlerTests.cs` | 3 | NEW |
| `Unit/Http/VaultHttpClientFactoryExtensionsTests.cs` | 3 | NEW |
| `Unit/Security/VaultSslOptionsTests.cs` | 3 | NEW |
| **TOTAL** | **100 new** | **5 → 105** |

### Test Conventions

- xUnit + Moq + FluentAssertions
- Arrange-Act-Assert pattern
- One assertion focus per test
- Naming: `MethodName_Scenario_ExpectedBehavior`
- Moq for interface mocking, `Mock<HttpMessageHandler>` + `Protected()` for HTTP-level
- No test depends on another test's state

## Phase F: Cleanup (M1-M5, M8)

### Cleanups

| Item | Action | File |
|------|--------|------|
| M1 | Delete `Class1.cs` | repo root |
| M2 | Remove `<PackageReference Include="System.Text.Json" />` | csproj |
| M3 | Fix CS8619 warnings: `Data = secrets.ToDictionary<string, string, string?>(...)` | VaultConfigurationProvider.cs |
| M4 | Add `<summary>` tags before closing `</summary>` in Database/PKI backend ctors | DatabaseSecretBackend.cs, PkiSecretBackend.cs |
| M5 | Check HTTP status in `GetHealthAsync`, throw `VaultSealedException` for 501/503 | VaultClient.cs |
| M8 | Already addressed in Phase B | PkiSecretBackend.cs |

## File Summary

### New Files (10)

```
src/Http/VaultAuthDelegatingHandler.cs
src/Http/VaultHttpClientFactoryExtensions.cs
src/Refresh/VaultLeaseRenewer.cs
src/Security/VaultSslOptions.cs
tests/Unit/Http/VaultAuthDelegatingHandlerTests.cs
tests/Unit/Http/VaultHttpClientFactoryExtensionsTests.cs
tests/Unit/Http/VaultSslOptionsTests.cs (was Security)
tests/Unit/Authentication/AppRoleAuthProviderTests.cs
tests/Unit/Authentication/KubernetesAuthProviderTests.cs
tests/Unit/Authentication/LdapAuthProviderTests.cs
tests/Unit/Core/VaultClientTests.cs
tests/Unit/Core/VaultConfigurationProviderTests.cs
tests/Unit/Core/VaultConfigurationSourceTests.cs
tests/Unit/Core/Exceptions/ExceptionTests.cs
tests/Unit/Backends/KvSecretBackendTests.cs
tests/Unit/Backends/DatabaseSecretBackendTests.cs
tests/Unit/Backends/PkiSecretBackendTests.cs
tests/Unit/Refresh/SecretRefresherTests.cs
tests/Unit/Refresh/VaultLeaseRenewerTests.cs
tests/Unit/HealthChecks/VaultHealthCheckTests.cs
tests/Unit/Security/VaultSslOptionsTests.cs
```

### Modified Files (12)

```
src/Core/VaultOptions.cs                  # Add Ssl property
src/Core/VaultClient.cs                   # Use IHttpClientFactory, fix health check
src/Core/VaultConfigurationSource.cs      # Use IHttpClientFactory, inject handlers
src/Core/VaultConfigurationProvider.cs    # Subscribe to event, remove timer, fix nullability
src/Authentication/AppRoleAuthProvider.cs # Add SemaphoreSlim, use factory
src/Authentication/KubernetesAuthProvider.cs # Same
src/Authentication/LdapAuthProvider.cs    # Same
src/Backends/KvSecretBackend.cs           # Use factory, call TrackSecret
src/Backends/DatabaseSecretBackend.cs     # Same, fix XML doc
src/Backends/PkiSecretBackend.cs          # Same, fix XML doc, fix ttl JSON
src/Refresh/SecretRefresher.cs            # Add renewer, ConcurrentDictionary, Interlocked
DotNet.Vault.Configuration.csproj         # Remove System.Text.Json, add Polly
README.md                                 # Add v1.0.1 migration section + SSL example
```

### Deleted Files (1)

```
Class1.cs                                # Placeholder from initial scaffold
```

## Verification Strategy

### Per-Phase Verification

After each phase:
1. `dotnet build` succeeds
2. New tests for the phase pass
3. All previous tests still pass (no regression)
4. Smoke test (Token + KV v2) still passes

### Final Verification (after all phases)

1. `dotnet build` - 0 errors, 0 warnings
2. `dotnet test /p:CollectCoverage=true` - all 105 tests pass, coverage > 80%
3. Smoke test against real Vault - PASSES
4. Tag v1.0.1

### Coverage Target

- Line coverage > 80%
- Branch coverage > 70%
- All public methods covered
- All exception paths covered

## Migration from v1.0.0 → v1.0.1

### Breaking Changes
**None** - despite "clean slate" approval, all public API changes are additions or use new patterns without removing old ones.

### New Features
- `VaultSslOptions` configuration
- Automatic `X-Vault-Token` attachment via `DelegatingHandler`
- Polly retry policy (was dead code, now active)
- Lease renewal via `/v1/sys/leases/renew`

### Internal Improvements
- 1 named HttpClient instead of 4
- Thread-safe auth provider token caching
- `SecretRefresher` properly tracks leases
- `OnSecretsRefreshed` event-driven refresh

### Cleanup
- 5 warnings → 0 warnings
- `Class1.cs` removed
- Test coverage 5% → 80%+

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| HttpClient factory refactor breaks auth | Medium | High | Phase A tests + smoke test |
| Lease renewal endpoint differs across Vault versions | Low | Medium | Catch exceptions, fall back to re-fetch |
| Thread-safety changes cause deadlock | Low | High | Use `SemaphoreSlim(1,1)` (non-fair), comprehensive tests |
| Test expansion reveals real bugs | Medium | Medium | Bug fixes inline before next phase |
| SSL config breaks http Vault connections | Low | Low | SSL options are opt-in, defaults are no-op |

## References

- v1.0.0 spec: `docs/superpowers/specs/2026-07-21-vault-configuration-design.md`
- v1.0.0 plan: `docs/superpowers/plans/2026-07-21-vault-configuration.md`
- v1.0.0 progress ledger: `.superpowers/sdd/progress.md` (Critical Fixes + Known Followups sections)
- v1.0.0 final review: agent `FinalReviewer` in conversation history
