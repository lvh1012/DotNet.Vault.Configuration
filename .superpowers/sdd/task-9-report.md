# Task 9 Report: Lease Renewal trong SecretRefresher

## Trạng thái
Hoàn tất.

## Thay đổi
- `SecretRefresher` nhận `VaultLeaseRenewer` và dùng một `ISecretRefreshScheduler` được đăng ký singleton.
- Metadata lease dùng `ConcurrentDictionary<string, SecretMetadata>` với metadata immutable và `TryUpdate`, tránh ghi đè metadata vừa được re-fetch đồng thời.
- Cờ refresh dùng `Interlocked.CompareExchange`/`Interlocked.Exchange` để loại trừ các refresh cycle chồng chéo.
- Mỗi cycle đến hạn renew lease renewable trước khi gọi subscriber reload. Renewal thất bại đánh dấu lease là non-renewable, vì vậy các cycle sau tiếp tục re-fetch thay vì retry renewal.
- `VaultConfigurationSource` đăng ký scheduler singleton và `VaultLeaseRenewer` để DI tạo được `SecretRefresher`.
- Bổ sung behavior tests cho thứ tự renew/reload, fallback sau renewal failure, non-renewable reload và default DI registration.

## Xác minh
- Focused refresh/source/provider tests: Passed 8/8.
- `dotnet test tests/DotNet.Vault.Configuration.Tests/DotNet.Vault.Configuration.Tests.csproj --no-restore`: Passed 32/32.
- `dotnet build DotNet.Vault.Configuration.csproj --no-restore`: thành công, 0 errors; còn 1 cảnh báo `NU1510` về `System.Text.Json` PackageReference hiện có.
- BasicExample smoke đã được thử bằng `dotnet run --project examples/BasicExample/BasicExample.csproj --no-restore`; thất bại vì không có Vault cục bộ tại `localhost:8200` (`Connection refused`).

## Quan ngại
Không có quan ngại về implementation. Smoke test không thể hoàn tất do phụ thuộc Vault cục bộ không chạy.
