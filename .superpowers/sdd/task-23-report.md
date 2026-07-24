# Task 23 Report: VaultHealthCheck tests

## Status

Completed. Added exactly seven deterministic `VaultHealthCheck` observable-contract tests.

## Coverage

1. Active, initialized, unsealed Vault with valid authentication returns `Healthy` and exposes Vault diagnostics.
2. A Vault standby response (`429`) remains `Healthy`.
3. A not-initialized Vault response (`501`) returns `Unhealthy`.
4. A sealed Vault response (`503`) returns `Unhealthy`.
5. Invalid Vault authentication returns `Degraded`.
6. A tracked secret with a 4:59 TTL returns `Degraded`.
7. A cancelled health check propagates `OperationCanceledException`.

## Defects exposed and fixed

- Vault emits `cluster_name`, `cluster_id`, and `server_time_utc`; `VaultHealthResponse` did not map these snake_case fields, causing health-check diagnostics to be empty/default. Added `JsonPropertyName` mappings.
- `VaultClient.GetHealthAsync` wrapped caller cancellation in `VaultConnectionException`, and `VaultHealthCheck` converted it to `Unhealthy`. Both now preserve caller-requested cancellation while retaining the established handling of non-cancellation failures.

The HTTP health-status policy intentionally remains unchanged; Task 28 owns that separate concern.

## Verification

- Focused `VaultHealthCheckTests`: 7 passed.
- `dotnet build DotNet.Vault.Configuration.csproj --no-restore`: passed (0 errors).
- `dotnet build tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore`: passed (0 errors).
- Full test project: 102 passed, 0 failed.

## Concern

The builds retain the existing `NU1510` warning for the direct `System.Text.Json` package reference. Task 26 is scheduled to remove that reference; it was intentionally not changed here.
