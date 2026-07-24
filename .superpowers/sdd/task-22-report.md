# Task 22 Report

## Status

Completed. `SecretRefresherTests` now contains exactly 11 deterministic observable-contract tests. They use the production `ISecretRefreshScheduler` seam and a controlled `IHttpClientFactory`/`HttpMessageHandler`; none uses timer delays or reflection.

## Coverage

1. Tracking a leased secret exposes its TTL.
2. Tracking an unleased secret creates no refresh work.
3. An explicit interval is scheduled unchanged.
4. An implicit interval uses 80% of the shortest tracked TTL.
5. No implicit interval is scheduled without a tracked TTL.
6. A cycle before the refresh threshold does not reload.
7. A cycle after the refresh threshold reloads.
8. A renewable lease is renewed before reload subscribers run.
9. Renewal failure prevents later renewal attempts while reloads continue.
10. Disabled refresh neither schedules nor reloads.
11. Disposal disposes the scheduler and blocks post-disposal reload work.

## Verification

- Focused: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~SecretRefresherTests --no-restore` — 11 passed, 0 failed.
- Library: `dotnet build DotNet.Vault.Configuration.csproj --no-restore` — succeeded, 0 errors.
- Test project: `dotnet build tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore` — succeeded, 0 errors.
- Full tests: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore` — 95 passed, 0 failed.

## Concerns

The verification commands report the existing NuGet warning `NU1510` for the explicit `System.Text.Json` PackageReference. No errors occurred; Task 22 does not modify package configuration.

## Commit

`HEAD` is the commit containing this Task 22 implementation and report.
