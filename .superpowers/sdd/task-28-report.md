# Task 28 Report — Vault health HTTP status classification

## Trạng thái

Hoàn tất. `VaultClient.GetHealthAsync` hiện phân loại status của `/v1/sys/health` theo semantics mặc định của HashiCorp Vault, thay vì deserialize mọi HTTP response như một health payload hợp lệ.

## TDD

- Đã thêm regression test `GetHealthAsync_WhenVaultIsSealed_ThrowsVaultSealedException` trước implementation; ban đầu fail vì không có exception được ném.
- Đã thêm contract tests cho standby, DR secondary, performance standby và unexpected HTTP error.

## Semantics đã sửa chính xác

- `200 OK`: Vault active, initialized, unsealed — deserialize và trả `VaultHealthResponse`.
- `429 TooManyRequests`: regular standby, initialized, unsealed — deserialize và trả response.
- `472`: disaster-recovery secondary, initialized, unsealed — deserialize và trả response.
- `473`: performance standby, initialized, unsealed — deserialize và trả response.
- `501 NotImplemented`: Vault uninitialized — deserialize và trả response để `VaultHealthCheck` báo `Vault is not initialized`.
- `503 ServiceUnavailable`: Vault sealed — `GetHealthAsync` ném `VaultSealedException`; `VaultHealthCheck` chuyển thành `Unhealthy("Vault is sealed")`.
- Mọi status khác: được coi là unexpected endpoint failure và được bọc trong `VaultConnectionException` với configured Vault URI.

Các mã status lấy từ HashiCorp Vault `/sys/health` API documentation: https://developer.hashicorp.com/vault/api-docs/system/health

## Tương thích được giữ

- Caller-requested `OperationCanceledException` vẫn được rethrow nguyên vẹn.
- Task 23 `snake_case` mappings (`cluster_name`, `cluster_id`, `server_time_utc`) không thay đổi.

## Verification

- Focused `GetHealthAsync` Release tests: 7 passed.
- `VaultHealthCheckTests` Release: 7 passed.
- Full Release test project: 115 passed, 0 failed.
- Release builds: library, test project và `examples/BasicExample` đều thành công.

## Concerns

M5 design cũ nêu `VaultSealedException` cho cả `501` và `503`. `501` thực tế biểu thị Vault chưa initialized, không phải sealed; xử lý nó như sealed sẽ phá contract health check hiện hữu. Implementation giữ phân biệt status thực tế của Vault.
