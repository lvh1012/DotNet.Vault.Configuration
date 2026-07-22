# Task 14 Fix Report: TLS Certificate Ownership

## Status
**DONE**

## Code Commit
`c6f06a6 fix: dispose path-loaded TLS certificates`

## Delivered
- Each primary handler that loads a client certificate or CA certificate from a configured path now wraps its `SocketsHttpHandler` in an owning handler.
- The owning handler disposes the inner handler before deterministically disposing every certificate it loaded.
- Directly supplied `X509Certificate2` instances remain caller-owned and are never added to the owning handler.
- Initialization failures dispose the `SocketsHttpHandler` and any certificates already loaded from paths.
- Added behavioral tests covering disposal of path-loaded client/CA certificates for both named clients and continued usability of directly supplied instances after disposal.

## TDD Evidence
- The new path-ownership test failed before the implementation with `Assert.Throws()` because the loaded certificate remained usable after primary-handler disposal.
- After the implementation, the focused TLS suite passed: **7/7**.

## Verification
- `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~VaultHttpClientFactoryExtensionsTests --no-restore` — passed (7/7).
- `dotnet test --no-restore` — passed (49/49).
- `dotnet build DotNet.Vault.Configuration.csproj --no-restore` — succeeded (0 errors).
- Focused review confirmed no further ownership, disposal-order, or named-client coverage findings.

## Concerns
- The build retains the pre-existing `NU1510` warning that `System.Text.Json` is likely unnecessary; this fix does not change package references.
- An initial solution-level `dotnet build --no-restore` invocation ended with an internal CLR error; rerunning the project build succeeded with no errors.
