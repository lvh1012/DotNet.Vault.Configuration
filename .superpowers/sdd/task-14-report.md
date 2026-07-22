# Task 14 Report: VaultSslOptions HTTP Handler

## Status
**DONE**

## Commit
`c425c32 feat: configure Vault HTTP SSL handler`

## Delivered
- Configured a `SocketsHttpHandler` as the primary handler for both `vault-client` and `vault-auth-client`.
- Applies TLS protocol, certificate-revocation mode, and `ServerName` (falling back to the Vault URI host).
- Loads PFX client certificates only when a configured path is present, or uses the supplied certificate instance.
- Loads CA certificates into an `X509ChainPolicy` custom-root trust store, preserving normal system trust when none is configured.
- Installs an accept-all certificate callback only when `SkipVerify` is explicitly `true`; secure defaults retain normal server validation.
- Added no-network unit tests for secure defaults and all configured SSL paths on both named clients.

## TDD Evidence
- New handler tests were run before implementation and failed because the existing handler left `EnabledSslProtocols` as `None`.
- After implementation, focused handler tests passed: **5/5**.

## Verification
- `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~VaultHttpClientFactoryExtensionsTests --no-restore` — passed (5/5).
- `dotnet test --no-restore` — passed (47/47).
- `dotnet build --no-restore` — succeeded (0 errors).

## Concerns
- Build emits the pre-existing `NU1510` warning that `System.Text.Json` is likely unnecessary. This task does not alter package references.
- The handler deliberately does not log certificate paths or passwords; no secrets are written to logs.
