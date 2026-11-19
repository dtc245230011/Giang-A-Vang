# Báo cáo tuần 1 — Phân tích và khởi tạo

- **Thời gian hoàn thành:** 2026-11-19
- **Nhánh:** `feature/week-01-foundation`
- **Trạng thái:** Hoàn thành 6/6 task

## 1. Task đã hoàn thành

| Task | Nội dung | Sản phẩm | Trạng thái |
|---|---|---|---|
| W1-01 | Phân tích yêu cầu, vai trò, phạm vi | `docs/requirements.md` | Hoàn thành |
| W1-02 | Quy trình nghiệp vụ, quy tắc trạng thái và phân công | `docs/business-rules.md` | Hoàn thành |
| W1-03 | Use Case, ERD, kiến trúc bằng Mermaid | `docs/architecture.md` | Hoàn thành |
| W1-04 | Khởi tạo solution API, Worker, Contracts, Tests | `SupportTicket.sln`, `src/`, `tests/` | Hoàn thành |
| W1-05 | Docker Compose cho SQL Server và RabbitMQ | `docker-compose.yml`, `.env.example` | Hoàn thành |
| W1-06 | README, `.gitignore`, kế hoạch 7 tuần | `README.md`, `.gitignore`, `docs/plan-7-weeks.md` | Hoàn thành |

**Chưa hoàn thành:** không có task bắt buộc nào của tuần 1 bị bỏ dở.
Các phần như API nghiệp vụ, RabbitMQ, SignalR, CI được chủ động để sang tuần 2–6 theo kế hoạch.

## 2. File và chức năng chính đã bổ sung

### Mã nguồn

- `src/SupportTicket.Api/Program.cs`: API tối giản với `/health`, `/api/info`, `/` chuyển hướng,
  OpenAPI ở môi trường Development; có `public partial class Program` để test tích hợp.
- `src/SupportTicket.Worker/Worker.cs`: worker nền ghi log khởi động và nhịp tim,
  chu kỳ cấu hình qua `Worker:HeartbeatSeconds` (mặc định 30 giây).
- `src/SupportTicket.Contracts/`: project dùng chung, sẽ chứa hợp đồng message ở tuần 3.
- `tests/SupportTicket.Tests/ApiSmokeTests.cs`: 2 test tích hợp cho `/health` và `/api/info`.

### Hạ tầng và cấu hình

- `docker-compose.yml`: SQL Server 2022 và RabbitMQ 4 management, volume bền vững,
  healthcheck, cổng lấy từ biến môi trường.
- `.env.example`: mẫu biến môi trường; `.env` thật đã được gitignore.
- `.gitignore`: loại bỏ `bin/`, `obj/`, `.env`, IDE và file tạm.

### Tài liệu

- Yêu cầu, quy tắc nghiệp vụ, kiến trúc (Use Case, ERD, sơ đồ tuần tự), kế hoạch 7 tuần.

## 3. Lệnh kiểm tra thực tế và kết quả

| Lệnh | Kết quả |
|---|---|
| `dotnet build SupportTicket.sln -v minimal` | **Build succeeded — 0 Warning, 0 Error** |
| `dotnet test SupportTicket.sln --no-build -v minimal` | **Passed 2/2**, Failed 0, Skipped 0 |
| Chạy API và `GET /health` | `HTTP 200`, nội dung `Healthy` |
| Chạy API và `GET /api/info` | `HTTP 200`, JSON có `service=SupportTicket.Api`, `status=running`, `environment=Development` |
| `GET /openapi/v1.json` | `HTTP 200` |
| Chạy Worker 5 giây | Log: `Assignment Worker started. Heartbeat every 30s...` |
| `docker compose config --quiet` | Không lỗi cấu hình |
| `docker compose up -d` | Tải và khởi động 2 container thành công |
| `docker compose ps` | `support-ticket-rabbitmq` **healthy**, `support-ticket-sqlserver` **healthy** |
| RabbitMQ Management API | Trả `RabbitMQ 4.4.1`, Erlang/OTP 29 |
| `sqlcmd -Q "SELECT @@VERSION"` | `Microsoft SQL Server 2022 (RTM-CU28) 16.0.4325.4 — Developer Edition (Linux)` |

Lưu ý: lần build đồng thời với tiến trình Worker đang chạy bị khóa file DLL; đã dừng tiến trình
và build lại thành công. Kết quả cuối cùng ở bảng trên là lần chạy sạch.

## 4. Cách chạy và demo

```bash
# 1. Hạ tầng
cp .env.example .env        # đổi mật khẩu trong .env
docker compose up -d
docker compose ps           # chờ cả hai healthy

# 2. Build và test
dotnet build
dotnet test

# 3. API (terminal 1)
dotnet run --project src/SupportTicket.Api
# mở http://localhost:5026/health và /api/info

# 4. Worker (terminal 2)
dotnet run --project src/SupportTicket.Worker
```

Kịch bản demo tuần 1:

1. Mở RabbitMQ Management tại <http://localhost:15672> bằng tài khoản trong `.env`.
2. Gọi `/health` và `/api/info` để chứng minh API hoạt động.
3. Chạy Worker, chỉ ra log khởi động và nhịp tim.
4. Chạy `dotnet test`, chỉ ra 2 test đạt.

## 5. Lỗi và hạn chế còn lại

- Chưa có API nghiệp vụ (`/tickets`, `/auth/login`, …) — theo kế hoạch tuần 2.
- Chưa kết nối EF Core/SQL Server và RabbitMQ từ mã nguồn — tuần 2–3.
- Chưa có giao diện và SignalR — tuần 5.
- Chưa có idempotency, retry, DLQ, outbox, CI — tuần 6.
- Repository chưa có remote GitHub và máy chưa cài `gh` CLI, nên **chưa push và chưa tạo PR**.
- SQL Server và RabbitMQ hiện vẫn đang chạy trên máy; dừng bằng `docker compose down`.
- Log Worker dùng tiếng Anh để tránh lỗi hiển thị tiếng Việt trên console Windows.

## 6. Công việc tuần tiếp theo (tuần 2)

1. Tạo 5 entity và cấu hình quan hệ.
2. Tạo `AppDbContext`, migration đầu tiên và dữ liệu mẫu (3 vai trò, 4 nhóm).
3. Xây dựng đăng nhập JWT và phân quyền.
4. Hoàn thiện `POST /tickets`, danh sách, chi tiết, ticket theo người dùng.
5. Bổ sung validation, phân trang và test quyền truy cập.
6. Tạo nhánh `feature/week-02-database-api` từ `main` (sau khi tuần 1 được review/merge).

## 7. Commit của tuần 1

Các commit đã tạo trên nhánh `feature/week-01-foundation`:

| Commit | Nội dung |
|---|---|
| `fbfd627` | `chore: initialize repository and ignore build artifacts` (trên `main`) |
| `65cc827` | `chore: initialize solution with API, worker and tests (W1-04)` |
| `6027770` | `chore: add SQL Server and RabbitMQ compose infrastructure (W1-05)` |
| `c567879` | `docs: add requirements and business rules (W1-01, W1-02)` |
| `b879af8` | `docs: add use case, ERD and architecture diagrams (W1-03)` |
| `cdd6a1c` | `docs: add README and seven-week plan (W1-06)` |

Báo cáo này được commit riêng sau khi các kiểm tra trên đã đạt.
