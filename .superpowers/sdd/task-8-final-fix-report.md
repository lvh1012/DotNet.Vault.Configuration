# Task 8 Final Fix Report

## Status

DONE_WITH_ENVIRONMENTAL_SMOKE_CONCERN

## Delivered

- Moved shared `SecretRefresher` startup from `VaultConfigurationSource` into `VaultConfigurationProvider` immediately after the successful initial secret load. Backend lease tracking therefore completes before scheduling begins, and an interlocked guard preserves exactly-once startup.
- Restored the default refresh interval: when `Refresh.Interval` is `null`, `SecretRefresher` schedules at 80% of the shortest tracked lease TTL. It does not create a default five-minute scheduler when no lease TTL is tracked. An explicit interval remains authoritative.
- Made `VaultConfigurationSource` require every `ServiceProviderFactory` result to implement `IDisposable` before resolving dependencies or transferring ownership to the configuration provider. Non-disposable results throw a deterministic `InvalidOperationException`.
- Retained exactly one scheduler: `SecretRefresher` owns the shared scheduler; `VaultConfigurationProvider` owns and disposes the DI provider, which disposes that scheduler. No provider-owned timer was added.

## TDD Evidence

- RED: `AddVault_WithLeasedSecretAndNoConfiguredInterval_SchedulesSharedRefresherAtEightyPercentOfTtl_AndStopsAfterDisposal` expected a 48-second interval for a one-minute lease but observed the prior five-minute default.
- RED: `Build_WithNonDisposableServiceProviderFactoryResult_ThrowsDeterministicOwnershipError` expected the ownership error but observed dependency-resolution failure instead.
- GREEN: both focused lifecycle tests pass after the lifecycle and ownership changes. The leased-secret test also verifies one scheduler start, refresh/reload behavior, and no further refresh after disposal.

## Verification

- Focused: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~VaultConfigurationSourceTests` — passed, 2 tests.
- Full: `dotnet test --no-restore` — passed, 28 tests, 0 failures.
- Build: `dotnet build --no-restore` — succeeded, 0 errors; existing `NU1510` warning for `System.Text.Json` remains.
- Smoke attempted: `dotnet run --project examples/BasicExample/BasicExample.csproj --no-build` — failed because local Vault at `localhost:8200` refused the connection (`SocketException 111`). This is an environment limitation; unit tests cover the lifecycle path without local Vault.

## Concerns

- End-to-end validation against a real Vault instance remains unavailable because no Vault is listening on `localhost:8200`.
