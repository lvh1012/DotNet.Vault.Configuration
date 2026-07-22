# Task 8 Fix Report

## Status
DONE_WITH_CONCERNS

## Fix

`VaultConfigurationSource` khởi động non-blocking `SecretRefresher` dùng chung sau khi tạo `VaultConfigurationProvider`; không có provider-owned timer thứ hai. Provider nhận quyền sở hữu tùy chọn đối với `ServiceProvider` do source tạo và giải phóng nó trong `Dispose`, khiến DI container dispose `SecretRefresher` và repeating timer. Nếu `Load` với `FailFast` ném lỗi trong khi `ConfigurationBuilder.Build()` đang tạo root, provider cũng dispose source-owned container trước khi ném lại lỗi.

`SecretRefresher.Dispose` đặt trường timer về `null` sau khi dispose, giúp trạng thái vòng đời rõ ràng và kiểm chứng được.

## TDD Evidence

- RED: `VaultConfigurationSourceTests.Build_RefreshEnabled_StartsSharedRefresher_AndDisposesItsTimer` thất bại vì `_refreshTimer` là `null` sau đường đi `ConfigurationBuilder.AddVault(...).Build()`.
- GREEN: test tập trung này pass sau khi source khởi động shared refresher và provider sở hữu source `ServiceProvider`.
- RED: `VaultConfigurationSourceTests.Load_FailFastFailure_DisposesSourceOwnedRefresherTimer` thất bại vì timer vẫn còn sau lỗi kết nối FailFast.
- GREEN: test pass sau khi `VaultConfigurationProvider.Load` dispose source-owned services trước khi ném lại lỗi.

## Files Changed

- `src/Core/VaultConfigurationSource.cs`
- `src/Core/VaultConfigurationProvider.cs`
- `src/Refresh/SecretRefresher.cs`
- `tests/DotNet.Vault.Configuration.Tests/Unit/Core/VaultConfigurationSourceTests.cs`

## Verification

- Focused: `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --filter FullyQualifiedName~VaultConfigurationSourceTests`: passed, 2 passed.
- `dotnet test`: passed, 28 passed, 0 failed.
- `dotnet build`: passed, 0 errors; 2 existing NU1510 warnings about `System.Text.Json` not being pruned.
- Review độc lập: không có Critical, Important, hoặc Minor finding.
- Smoke: `dotnet run --project examples/BasicExample/BasicExample.csproj` thất bại vì `localhost:8200` từ chối kết nối (`HttpRequestException` / `SocketException 111`). Vault cục bộ không khả dụng.

## Concern

Không thể xác nhận end-to-end với Vault thật trong môi trường hiện tại vì Vault tại `localhost:8200` không lắng nghe. Lifecycle và đường đi cấu hình được kiểm chứng bằng unit tests không phụ thuộc Vault cục bộ.
