# Task 10 Report

## Status
Completed. `AppRoleAuthProvider` now uses a `SemaphoreSlim` with an outside/inside cache double-check so concurrent cache misses coalesce to one AppRole login. The cache is stored as an immutable snapshot and read/written atomically; `Dispose` releases the semaphore. The unauthenticated `VaultAuthClientName` login client remains unchanged.

## Tests
- TDD red: `GetTokenAsync_ConcurrentCalls_OnlyOneLoginHappens` failed before the implementation with `Expected: 1; Actual: 10` HTTP logins.
- Focused: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~AppRoleAuthProviderTests --no-restore` — passed, 3/3.
- Full: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore` — passed, 35/35.
- Build: `dotnet build DotNet.Vault.Configuration.csproj --no-restore` — succeeded with 0 errors and 1 pre-existing `NU1510` warning for `System.Text.Json`.

## BasicExample smoke
Attempted `dotnet run --project examples/BasicExample/BasicExample.csproj --no-restore`.

Exact result: failed with `Smoke test FAILED: Connection refused (localhost:8200)` (`HttpRequestException`, inner `SocketException (111)`), because no Vault server was listening at the example's configured `http://localhost:8200` endpoint.

## Concern
The smoke test could not reach a local Vault instance; this is environmental and unrelated to the AppRole cache implementation.
