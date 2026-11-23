# Báo cáo kiểm thử thực tế

> Ngày chạy: 2026-11-23 (giờ UTC).  
> Môi trường: Windows 11 Pro, .NET SDK 10.0.403, SQL Server 2022 CU28 Developer (Docker),
> RabbitMQ 4.4.1 (Docker), Docker 29.9.2 / Compose v5.6.1, Node.js 24.19.0, Chromium 136 (Playwright 1.52).
>
> Nguyên tắc: chỉ ghi “đạt” cho kiểm tra đã chạy. Các luồng phân tán được kiểm chứng bằng RabbitMQ,
> Assignment Worker và SignalR thật, không dùng mock làm bằng chứng.

## 1. Tổng hợp kết quả

| Hạng mục | Kết quả | Bằng chứng |
|---|---|---|
| Build solution | **Đạt** | `dotnet build SupportTicket.sln` — 0 lỗi, 0 cảnh báo bảo mật NuGet sau khi pin bản vá |
| Unit + integration test | **Đạt 47/47** | `RUN_INTEGRATION_TESTS=1 dotnet test` — 0 failed, 0 skipped, ~15 giây |
| Postman/Newman | **Đạt** | 18 request, 17 assertion, 0 failed |
| E2E RabbitMQ + Worker + SignalR (Docker) | **Đạt** | Tạo ticket → Assigned → InProgress → Resolved; customer nhận thông báo, customer2 không nhận |
| Worker dừng rồi chạy lại | **Đạt** | Message nằm trong queue khi worker tắt; chạy lại → Assigned |
| RabbitMQ gián đoạn, Outbox gửi lại | **Đạt** | Outbox pending, retry=3; broker phục hồi → processed=2, ticket Assigned |
| Retry có giới hạn + DLQ | **Đạt** | Message lỗi vào `assignment-worker.dlq` sau số lần thử; DLQ được dọn |
| Giao diện Playwright (Docker) | **Đạt** | Đăng ký, đăng nhập, tạo ticket, dashboard, admin, realtime, tìm kiếm, mobile; 0 lỗi console |
| Dữ liệu sau restart container | **Đạt** | Ticket `TK-261123-J9UB` vẫn Resolved, lịch sử 6 bản ghi |
| Healthcheck Docker | **Đạt** | SQL/Rabbit/API healthy, Worker Up |
| Lỗ hổng package NuGet | **Đã xử lý** | `System.Security.Cryptography.Pkcs` 9.0.11 → 10.0.14; cảnh báo NU1902/NU1903 không còn |

## 2. Kiểm thử đơn vị và tích hợp (xUnit)

### 2.1. Cách chạy

```bash
set -a && . ./.env && set +a
RUN_INTEGRATION_TESTS=1 dotnet test SupportTicket.sln
```

Không đặt `RUN_INTEGRATION_TESTS=1` thì các test cần SQL Server được **skip** có ghi lý do; unit test vẫn chạy.

Test factory tạo database riêng `SupportTicketTest_<random>`, **tắt RabbitMQ/Outbox/Notification consumer**,
kiểm tra `DB_NAME()` sau khởi động để chắc chắn không chạm database phát triển, rồi xóa database test sau khi chạy.
Vì vậy test API không dùng mock để chứng minh luồng RabbitMQ; luồng đó được chứng minh ở mục 3 và 4.

### 2.2. Nhóm test và nội dung

| File | Số test | Nội dung chính |
|---|---|---|
| `UnitRulesTests.cs` | 16 | Ánh xạ 4 danh mục + nhánh mặc định; ma trận chuyển trạng thái; định dạng mã ticket |
| `ApiSmokeTests.cs` | 5 | Health, info, frontend tĩnh, tương thích `/api` và `/tickets`, cấu hình messaging |
| `AuthTests.cs` | 8 | Đăng nhập Admin, sai mật khẩu, đăng ký luôn Customer dù gửi `role=Admin`, email trùng, tài khoản bị khóa, 401, Admin không tự khóa, SignalR negotiate cần JWT |
| `TicketFlowTests.cs` | 14 | Tạo ticket + lịch sử + Outbox; validation; cô lập Customer; Agent không thấy ticket chưa giao; Admin gán → Agent xử lý → Resolved; chuyển sai; cập nhật đồng thời; lọc/phân trang; worker 4 nhóm; idempotent; thiếu nhóm thì rollback rồi retry |
| `AdminApiTests.cs` | 4 | Chặn non-admin; tạo Agent + nhóm; vô hiệu hóa chặn đăng nhập; CRUD nhóm/thành viên; dữ liệu thật |

Kết quả cuối:

```text
Passed!  - Failed: 0, Passed: 47, Skipped: 0, Total: 47, Duration: 15 s
```

Các tình huống quan trọng đã kiểm chứng:

- Đăng ký công khai gửi kèm `"role": "Admin"` vẫn nhận `role = Customer`.
- Customer không đọc được chi tiết, danh sách hoặc `/users/{id}/tickets` của người khác.
- Agent không xem được ticket `New` chưa phân công; Admin xem được.
- Ticket `New → InProgress`, `Assigned → Resolved`, cập nhật bởi Customer đều bị chặn đúng mã.
- Hai request đổi trạng thái đồng thời với cùng `rowVersion`: **một 200, một 409**.
- Cùng `MessageId` giao lại: `Duplicate`, không thêm lịch sử/sự kiện. Message mới cho ticket đã giao: `AlreadyAssigned`, không ghi đè.
- Thiếu nhóm đích: transaction rollback, ticket vẫn `New`, không có Inbox; tạo lại nhóm rồi xử lý cùng message thành công.
- Lịch sử tạo ticket + phân công + 2 lần đổi trạng thái = 6 bản ghi, đúng thứ tự nhóm → người → trạng thái.

## 3. E2E thật: RabbitMQ → Assignment Worker → SignalR

### 3.1. Kịch bản

1. Đăng nhập Customer, Customer2, Agent Kỹ thuật; mở 3 kết nối SignalR bằng JWT.
2. Customer tạo ticket danh mục `Account`.
3. Outbox gửi `TicketCreated`; Worker trong container riêng đọc queue và phân công.
4. Customer + Agent nhận `TicketAssigned`; Customer2 không nhận.
5. Agent chuyển `InProgress`, rồi `Resolved`; Customer nhận từng sự kiện.
6. Kiểm tra lịch sử, danh sách và độ sâu queue.

### 3.2. Kết quả (stack Docker)

```text
Đã tạo: TK-261123-TZUT status=New
[SignalR:agent.tech] TicketAssigned ... status=Assigned
[SignalR:customer] TicketAssigned ... status=Assigned
Phân công: Kỹ thuật / Nguyễn Văn Kỹ Thuật
customer2 không nhận thông báo: OK
[SignalR:customer] TicketStatusChanged ... status=InProgress
Nhân viên thực hiện không nhận lại thông báo của chính mình: OK
[SignalR:customer] TicketStatusChanged ... status=Resolved
Lịch sử (6): Status ∅→New; AssignedTeamId ∅→TECH; AssignedUserId ∅→Nguyễn Văn Kỹ Thuật;
             Status New→Assigned; Status Assigned→InProgress; Status InProgress→Resolved
assignment-worker: messages=0, consumers=1
notification-service: messages=0, consumers=1
retry/DLQ: messages=0
E2E PASSED
```

Kịch bản tương tự cũng đã chạy đạt với API/Worker chạy trực tiếp bằng `dotnet run` trên máy.

## 4. Kiểm thử độ tin cậy

### 4.1. Worker dừng khi có ticket mới

- Dừng tiến trình Worker, tạo ticket `Payment` → `TK-261123-3DTA`.
- Kiểm tra: ticket `New`; RabbitMQ `assignment-worker` có `messages=1, ready=1, consumers=0`.
- Chạy lại Worker: ticket chuyển `Assigned`, nhóm `ACCT`, người xử lý `Phạm Thị Kế Toán`.
- Kết luận: message không mất khi worker offline; worker xử lý tiếp khi trở lại.

### 4.2. RabbitMQ gián đoạn và Outbox

- Dừng container RabbitMQ, tạo ticket `Delivery` → `TK-261123-CFAD`.
- Truy vấn SQL Server:

```text
TICKET=TK-261123-CFAD STATUS=New
OUTBOX_PENDING=1
OUTBOX_RETRY=1
```

- Khởi động lại RabbitMQ (healthy), chạy Worker.
- Kết quả:

```text
TICKET STATUS=Assigned TEAM=CSKH
OUTBOX_UNPROCESSED=0
OUTBOX_PROCESSED=2        (TicketCreated + TicketAssigned)
OUTBOX_RETRY=3
```

- Kết luận: API vẫn nhận ticket khi broker lỗi; Outbox tự gửi lại sau khi broker phục hồi; không mất sự kiện.

### 4.3. Retry có giới hạn và DLQ

Gửi message có `type=TicketCreated`, payload JSON không hợp lệ vào exchange `support-tickets`, routing key `ticket.created`:
- Worker thử xử lý, đưa vào retry queue TTL 5 giây, lặp lại có giới hạn.
- Sau số lần thử cấu hình, message vào `assignment-worker.dlq`:

```text
DLQ RECEIVED: main=0 retry=0 dlq=1
```

- DLQ sau đó được dọn để không ảnh hưởng demo. Không có vòng lặp requeue vô hạn.

### 4.4. Cập nhật đồng thời

Trong bộ test tích hợp, hai PATCH trạng thái dùng cùng `rowVersion` được gửi song song:
- Đúng một request nhận `200 OK`, request còn lại nhận `409 Conflict`.
- Trạng thái cuối là `InProgress`, không ghi đè lẫn nhau.

### 4.5. Dữ liệu sau khởi động lại

- Ticket giao diện `TK-261123-J9UB` đã `Resolved`.
- `docker compose restart api`, chờ healthy, đăng nhập lại và gọi API:

```text
after restart: TK-261123-J9UB Resolved Kỹ thuật Nguyễn Văn Kỹ Thuật resolvedAt=...
history after restart: 6 rows
```

- Volume SQL Server giữ dữ liệu; migration không xóa/tạo lại CSDL.

### 4.6. Idempotency

- Cùng `MessageId` giao lại → `Duplicate`; số lịch sử và sự kiện không tăng.
- Message mới cho ticket đã được phân công → `AlreadyAssigned`; phân công thủ công không bị ghi đè.
- Notification consumer kiểm tra `InboxMessages` trước khi gửi; một sự kiện chỉ gửi một lần cho mỗi người.

## 5. Kiểm thử giao diện bằng Playwright

Kịch bản tự động trên Chromium, chạy với stack Docker:

| Bước | Kết quả |
|---|---|
| Đăng ký công khai qua form | Tạo tài khoản, vào dashboard, vai trò hiển thị **Khách hàng** |
| Đăng xuất | Quay về `login.html` |
| Đăng nhập Customer | Dashboard 4 thẻ số liệu, SignalR “Đã kết nối” |
| Tạo ticket qua form | Thành công, hiện mã ticket, mở được chi tiết |
| Worker phân công | Chi tiết tự hiển thị “Đã phân công” |
| Admin | Danh sách tài khoản, 4+ nhóm, tạo nhóm mới, mở modal thành viên |
| Agent | Mở ticket, “Bắt đầu xử lý”, nhập ghi chú, trạng thái InProgress |
| Customer realtime | Toast + chuông thông báo, trạng thái tự cập nhật, không tải lại thủ công |
| Agent hoàn thành | Trạng thái Resolved; customer thấy ngay |
| Timeline | 6 mục, thứ tự hợp lý, có ghi chú và người thực hiện |
| Tìm kiếm danh sách | Nhập mã ticket trả đúng 1 dòng |
| Responsive 390×844 | Dashboard hiển thị, sidebar mobile mở/đóng được |
| Lỗi JavaScript | **0 lỗi console/pageerror** sau khi sửa thuộc tính `pattern` HTML |

Ảnh chụp tại `docs/screenshots/`:

| File | Nội dung |
|---|---|
| `00-register-customer.png` | Đăng ký công khai thành công |
| `01-dashboard-customer.png` | Dashboard khách hàng |
| `02-ticket-created.png` | Tạo ticket thành công |
| `03-ticket-detail-assigned.png` | Chi tiết ticket đã phân công |
| `04-admin-users.png` | Quản trị tài khoản |
| `05-admin-teams.png`, `05b-admin-team-members.png` | Quản trị nhóm/thành viên |
| `06-agent-in-progress.png` | Nhân viên đang xử lý |
| `07-customer-realtime.png` | Toast + chuông realtime |
| `08-ticket-resolved-timeline.png` | Ticket hoàn thành + timeline |
| `09-mobile-dashboard.png`, `10-mobile-sidebar.png` | Responsive |
| `11-ticket-search.png` | Tìm kiếm danh sách |

## 6. Postman/Newman

- Collection: `postman/SupportTicket.postman_collection.json`.
- Environment mẫu: `postman/SupportTicket.postman_environment.json` (mật khẩu để trống, người dùng tự điền cục bộ).
- Chạy với stack Docker:

```text
requests: 18, failed: 0
test-scripts: 14, failed: 0
assertions: 17, failed: 0
average response time: 414 ms (min 12 ms, max 4.5 s)
```

Bao phủ: health, meta, đăng nhập 3 vai trò, teams/agents, tạo ticket, danh sách có lọc, chi tiết,
ticket theo người dùng, InProgress, Resolved, lịch sử, dashboard, danh sách tài khoản và đường dẫn không tiền tố `/tickets`.

Lệnh đã dùng (thay `$DEMO_PASSWORD` bằng mật khẩu trên máy):

```bash
npx newman run postman/SupportTicket.postman_collection.json \
  -e postman/SupportTicket.postman_environment.json \
  --env-var "adminPassword=$DEMO_PASSWORD" \
  --env-var "customerPassword=$DEMO_PASSWORD" \
  --env-var "agentPassword=$DEMO_PASSWORD" \
  --delay-request 2500
```

## 7. Kiểm tra bảo mật và cấu hình

- Không commit `.env`; `.env.example` chỉ chứa giá trị mẫu. JWT key/mật khẩu seed lấy từ biến môi trường.
- JWT yêu cầu khóa ≥ 32 byte; Production thiếu khóa sẽ dừng khởi động.
- Mỗi request có token đều kiểm tra `Users.IsActive`.
- Mật khẩu băm bằng `PasswordHasher<User>`, không trả hash; API không trả stack trace.
- Tài khoản seed không bị xóa; Admin vô hiệu hóa thay vì xóa.
- Danh mục package đã được pin bản vá `System.Security.Cryptography.Pkcs` 10.0.14;
  `dotnet restore/build` không còn cảnh báo NU1902/NU1903.
- Container chạy bằng user `app` của image .NET; image API/Worker không chứa `.env` (đã kiểm tra bằng `.dockerignore`).

## 8. Chưa kiểm chứng / hạn chế

- Chưa chạy kiểm thử tải, độ trễ với 10.000 ticket.
- Chưa chạy nhiều instance API/Worker cùng lúc; Outbox hiện chống trùng ở consumer, chưa khóa hàng khi lấy lô.
- Chưa kiểm thử phát lại message từ DLQ bằng công cụ giao diện (chỉ kiểm tra vào DLQ và dọn thủ công).
- Chưa có CI GitHub Actions; mọi lệnh trên chạy tại máy phát triển.
- Chưa có refresh token/SMS/email; thông báo offline không được lưu, giao diện tải lại qua API khi kết nối lại.
- Chưa triển khai lên hosting công khai (đúng phạm vi yêu cầu).
