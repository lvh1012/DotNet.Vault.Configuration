# Task 15 Report: VaultClient Tests

## Status
**DONE**

## Commit
`bc3b925 test: cover VaultClient behavior`

## Delivered
- Added exactly 10 `VaultClient` unit tests covering secret-backend routing, ordered backend selection, duplicate-key overwrite behavior, unsupported paths, authentication-provider selection, missing providers, health HTTP requests and transport failures, and token lookup validity outcomes.
- Tests exercise the named `vault-client`, `/v1/sys/health`, `/v1/auth/token/lookup-self`, and `X-Vault-Token` observable HTTP contracts.
- The new health test exposed a concrete defect: Vault's camel-case JSON fields did not populate the Pascal-case `VaultHealthResponse` properties. `GetHealthAsync` now deserializes with `JsonSerializerOptions.Web`, which applies web-compatible property-name handling.

## TDD Evidence
- The focused test run initially failed in `GetHealthAsync_UsesNamedClientHealthPathAndDeserializesResponse`: the JSON `initialized` field left `VaultHealthResponse.Initialized` as `false`.
- The minimal production fix was applied, then all 10 focused tests passed.

## Verification
- `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~VaultClientTests` — passed (10/10).
- `dotnet test` — passed (59/59).
- `dotnet build --no-restore` — succeeded (0 errors).

## Concerns
- Build and test output retain the pre-existing `NU1510` warning that the explicit `System.Text.Json` package reference is likely unnecessary. This task did not alter package references.
