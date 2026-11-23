# Checklist hoàn thiện hệ thống hỗ trợ khách hàng

> Cập nhật lần cuối: 2026-11-23  
> Nhánh triển khai: `feature/complete-customer-support`  
> Nguồn kế thừa: nhánh `feature/week-01-foundation` (bộ khung solution, Docker SQL Server/RabbitMQ, tài liệu phân tích).  
> Tài liệu này thay thế cách chia công việc theo tuần; trạng thái bên dưới dựa trên kiểm tra thực tế, không dựa trên kế hoạch.

## 1. Kế thừa từ tuần 1 và phần đã hoàn thiện thêm

| Hạng mục | Tuần 1 | Hiện tại |
|---|---|---|
| Solution `SupportTicket.sln` | Có API, Worker, Contracts, Tests | Giữ nguyên, bổ sung project `SupportTicket.Infrastructure` |
| SQL Server, RabbitMQ compose | Có image, volume, healthcheck | Giữ nguyên, bổ sung service API và Worker |
| API | `/health`, `/api/info`, OpenAPI | Thêm xác thực, ticket, người dùng, nhóm, dashboard, SignalR |
| Worker | Chỉ heartbeat | Consumer RabbitMQ thật, phân công, Outbox, retry, DLQ |
| Cơ sở dữ liệu | Chưa có | EF Core, migration, seed idempotent, RowVersion |
| Giao diện | Chưa có | HTML/CSS/JS + Bootstrap cục bộ, tiếng Việt, responsive |
| Kiểm thử | 2 smoke test | 47 test (đơn vị + tích hợp SQL thật) + E2E + UI Playwright |
| Tài liệu | Yêu cầu, quy tắc, kiến trúc, kế hoạch tuần | Bổ sung API, báo cáo test, kịch bản demo, hướng dẫn chạy |

## 2. Tám chức năng bắt buộc theo đề tài

| # | Chức năng | Trạng thái | Bằng chứng |
|---|---|---|---|
| 1 | Tạo yêu cầu hỗ trợ mới | Đã có | `TicketsController.Create`, `TicketService.CreateAsync`, form `new-ticket.html`; test `CreateTicket_PersistsEntityHistoryAndOutboxEvent`; UI test tạo ticket qua trình duyệt |
| 2 | Tra cứu danh sách và chi tiết | Đã có | `GET /tickets`, `GET /tickets/{id}`; `tickets.html`, `ticket.html`; test `List_SupportsSearchFiltersAndPagination`, UI test tìm kiếm |
| 3 | Đưa yêu cầu mới vào queue xử lý | Đã có | Outbox ghi cùng transaction; `OutboxPublisherService` gửi `TicketCreated`; test kiểm tra Outbox; E2E RabbitMQ thật |
| 4 | Worker gán nhóm/nhân viên theo quy tắc | Đã có | `AssignmentProcessor`, `AssignmentRules`; 4 test theory Account→TECH, Payment→ACCT, Delivery→CSKH, Other→GENERAL; E2E chứng minh worker chạy tiến trình riêng |
| 5 | Cập nhật trạng thái New→Assigned→InProgress→Resolved | Đã có | `StatusTransitionPolicy`, `TicketService.ChangeStatusAsync`; test luồng hợp lệ/không hợp lệ; UI test nhân viên bắt đầu và hoàn thành |
| 6 | Phát sự kiện khi trạng thái/người xử lý thay đổi | Đã có | Outbox `TicketAssigned`, `TicketStatusChanged`; test đếm sự kiện; E2E nhận đúng thứ tự |
| 7 | Thông báo realtime đúng người | Đã có | `NotificationHub`, `NotificationConsumerService`, `Clients.Users`; E2E customer nhận, customer2 không nhận, người thực hiện không nhận lại; UI test toast + chuông |
| 8 | Lưu lịch sử thay đổi | Đã có | `TicketHistories`; mỗi thay đổi có trường cũ/mới, người thực hiện/hệ thống, ghi chú, thời gian; timeline hiển thị 6 bản ghi trong E2E |

## 3. Năm API tối thiểu và API bổ sung

| API bắt buộc | Trạng thái | Ghi chú |
|---|---|---|
| `POST /tickets` | Đã có | Có thêm alias `/api/tickets`; trả `201`, `Location`, mã ticket |
| `GET /tickets` | Đã có | Phân trang, lọc trạng thái/ưu tiên/danh mục/nhóm, tìm kiếm mã hoặc tiêu đề |
| `GET /tickets/{id}` | Đã có | Kiểm tra quyền theo tài nguyên, trả `rowVersion` |
| `PATCH /tickets/{id}/status` | Đã có | Chỉ nhân viên/Admin có quyền, chặn chuyển sai, `409` khi xung đột |
| `GET /users/{userId}/tickets` | Đã có | Khách hàng/nhân viên chỉ xem của mình; Admin xem mọi người |

API bổ sung: `GET /tickets/{id}/history`, `PATCH /tickets/{id}/assignment`, `GET /teams`, quản lý thành viên nhóm, `GET/POST/PATCH /users`, `GET /users/agents`, `GET /dashboard/summary`, `GET /meta`, đăng ký/đăng nhập/đăng xuất. Danh sách đầy đủ tại [api.md](api.md).

Cả đường dẫn có và không có tiền tố `/api` đều được ánh xạ và có test `RequiredTicketPaths_WorkWithAndWithoutApiPrefix`.

## 4. Hạng mục kỹ thuật

| Hạng mục | Trạng thái | Bằng chứng / ghi chú |
|---|---|---|
| Bảng Users, Teams, TeamMembers, Tickets, TicketHistories | Đã có | `AppDbContext`, migration `InitialCreate` |
| Bảng OutboxMessages, InboxMessages | Đã có | Ghi cùng transaction, chống trùng theo `MessageId` |
| Khóa chính/ngoại, index, giới hạn độ dài | Đã có | Cấu hình trong `AppDbContext.OnModelCreating` |
| Migration, không xóa CSDL | Đã có | API chạy `MigrateAsync`, không `EnsureDeleted`; migration đã áp dụng thật |
| Seed idempotent, dữ liệu tồn tại sau restart | Đã có | Seed theo email/mã nhóm/mã ticket; đã restart container API và đọc lại ticket |
| Đăng ký công khai chỉ tạo Customer | Đã có | `AuthController.Register`; test gửi thêm `role=Admin` vẫn nhận Customer |
| Mật khẩu băm chuẩn | Đã có | `PasswordHasher<User>` (PBKDF2), không trả hash |
| JWT, logout, khóa tài khoản | Đã có | JWT Bearer, kiểm tra `IsActive` mỗi request, client xóa token khi logout |
| Phân quyền backend | Đã có | Kiểm tra ở service/endpoint; test Customer/Agent/Admin |
| Validation, ProblemDetails, mã HTTP | Đã có | DataAnnotations + kiểm tra nghiệp vụ; middleware lỗi thống nhất, không lộ stack trace |
| RowVersion, xử lý cập nhật đồng thời | Đã có | Test hai request đồng thời: một `200`, một `409` |
| RabbitMQ persistent, publisher confirm, mandatory | Đã có | `RabbitMqPublisher` dùng publisher confirmation tracking; message persistent |
| Retry có giới hạn + DLQ | Đã có | Test thủ công: message lỗi qua retry rồi vào DLQ; queue DLQ sau đó được dọn |
| Outbox gửi lại khi RabbitMQ gián đoạn | Đã có | Dừng RabbitMQ, tạo ticket, Outbox pending; bật lại broker, retry 3 lần rồi gửi thành công |
| Worker idempotent, không ghi đè phân công | Đã có | Test cùng MessageId → Duplicate; MessageId mới cho ticket đã giao → AlreadyAssigned |
| Worker dừng rồi chạy lại | Đã có | Ticket ở New, message ready trong queue; chạy lại worker → Assigned |
| SignalR xác thực, không tự join nhóm người khác | Đã có | Hub `[Authorize]`, server tự thêm group; client không có API join group |
| Gửi thông báo đúng người, nội dung tối thiểu | Đã có | Recipients = người tạo + người xử lý + thành viên nhóm, loại trừ người thực hiện; payload không có mô tả |
| Giao diện tiếng Việt, responsive, trạng thái loading/empty/error | Đã có | 13 ảnh chụp tại `docs/screenshots`; Playwright kiểm tra desktop/mobile |
| Không render HTML khách hàng | Đã có | Toàn bộ dữ liệu động dùng `textContent`/DOM API |
| Vô hiệu hóa thay vì xóa tài khoản/nhóm | Đã có | `IsActive`; không có API xóa người dùng |
| Docker Compose đầy đủ | Đã có | `sqlserver`, `rabbitmq`, `api`, `worker`; healthcheck, volume, env |
| README, API, Postman, báo cáo test, demo | Đã có | README, docs/api.md, docs/test-report.md, docs/demo-script.md, postman/ |

## 5. Kết quả kiểm chứng thực tế (ngày 2026-11-23)

| Kiểm tra | Kết quả |
|---|---|
| `dotnet build SupportTicket.sln` | Thành công, 0 lỗi |
| `dotnet test` với `RUN_INTEGRATION_TESTS=1` | **47/47 đạt**, 0 bỏ qua, khoảng 15 giây |
| Docker `docker compose up -d --build` | 4 container; SQL/Rabbit/API healthy, Worker chạy |
| `GET /health` qua Docker | `Healthy` |
| E2E RabbitMQ + Worker + SignalR (Docker) | **PASSED** — tạo → phân công → InProgress → Resolved |
| Outbox khi RabbitMQ dừng | Pending=1, Retry=3; broker phục hồi → Processed=2, ticket Assigned |
| Worker dừng khi có ticket | Queue `ready=1`, ticket New; chạy lại → Assigned |
| Retry/DLQ | Message lỗi vào DLQ sau số lần thử giới hạn; DLQ sau đó được dọn |
| UI Playwright (Docker) | **PASSED** — đăng ký, đăng nhập, tạo ticket, dashboard, admin tạo nhóm, realtime, tìm kiếm, mobile |
| Khởi động lại container API | Vé `TK-261123-J9UB` vẫn `Resolved`, đủ 6 bản ghi lịch sử |
| Hàng đợi sau E2E | main/retry/DLQ đều 0 message, consumer đang chạy |

Chi tiết lệnh và bằng chứng tại [test-report.md](test-report.md).

## 6. Hạn chế còn lại

- Outbox publisher chạy ở cả API và Worker; chống trùng dựa trên `InboxMessages` nên có thể có message trùng trên đường truyền (đã xử lý idempotent), chưa dùng khóa hàng `UPDLOCK/READPAST` cho đa instance.
- Chưa có refresh token, thu hồi token tức thời hoặc đổi mật khẩu tự phục vụ (Admin có thể đặt lại mật khẩu).
- DLQ chưa có công cụ phát lại trên giao diện; hiện xử lý qua RabbitMQ Management.
- Chưa kiểm thử tải/hiệu năng với 10.000 ticket và chưa có Redis backplane cho nhiều instance SignalR.
- Chưa triển khai CI GitHub Actions; các lệnh build/test đã chạy cục bộ.
- Không triển khai hosting công khai theo yêu cầu.

## 7. Cách tiếp tục nếu phiên làm việc bị gián đoạn

```bash
# 1. Trạng thái mã nguồn
git status
git log --oneline -5

# 2. Dựng lại và kiểm thử
dotnet build SupportTicket.sln
cp .env.example .env   # nếu chưa có, sau đó điền bí mật cục bộ
docker compose up -d --build
set -a && . ./.env && set +a
RUN_INTEGRATION_TESTS=1 dotnet test SupportTicket.sln
DEMO_PASSWORD="$DEMO_PASSWORD" node tools/e2e/realtime-e2e.mjs
DEMO_PASSWORD="$DEMO_PASSWORD" node tools/ui-check/ui-check.mjs
```

Các file cần đọc lại khi tiếp tục: `docs/completion-checklist.md`, `docs/test-report.md`, `src/SupportTicket.Api/Program.cs`, `src/SupportTicket.Worker/Worker.cs`, `src/SupportTicket.Infrastructure/Assignments/AssignmentProcessor.cs`.
