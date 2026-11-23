# Kế hoạch thực hiện 7 tuần (tài liệu lịch sử)

> **Lưu ý:** tài liệu này chỉ còn giá trị lịch sử của giai đoạn khởi tạo. Giai đoạn hoàn thiện **không chia
> công việc theo tuần**; trạng thái thực tế theo chức năng nằm tại [completion-checklist.md](completion-checklist.md).
>
> Dự án: Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ khách hàng  
> Cập nhật: 2026-11-19  
> Repository: `https://github.com/dtc245230011/Giang-A-Vang`

## 1. Quy ước

| Mục | Quy ước |
|---|---|
| Nhánh chính | `main` |
| Nhánh từng tuần | `feature/week-01-foundation`, `feature/week-02-database-api`, … |
| Commit | `type(scope): mô tả (Wx-yy)`, ví dụ `feat: add assignment worker (W3-04)` |
| Loại commit | `chore`, `feat`, `fix`, `test`, `docs`, `refactor` |
| Định nghĩa hoàn thành | Build + test đạt; tài liệu cập nhật; báo cáo tuần; commit đúng phạm vi; không có bí mật |
| PR | Tạo draft PR vào `main`; không tự merge |

## 2. Tổng quan

| Tuần | Milestone | Nhánh | Mục tiêu | Trạng thái |
|---|---|---|---|---|
| 1 | Khởi tạo nền tảng | `feature/week-01-foundation` | Phân tích, thiết kế, solution, Docker, tài liệu | Hoàn thành |
| 2 | Cơ sở dữ liệu và API | `feature/week-02-database-api` | Entity, migration, đăng nhập, API ticket | Chưa bắt đầu |
| 3 | RabbitMQ và Worker | `feature/week-03-messaging-worker` | Xếp hàng, phân công tự động | Chưa bắt đầu |
| 4 | Trạng thái và lịch sử | `feature/week-04-status-history` | Vòng đời ticket, phân quyền, lịch sử | Chưa bắt đầu |
| 5 | Giao diện và realtime | `feature/week-05-ui-realtime` | Giao diện, SignalR, thông báo | Chưa bắt đầu |
| 6 | Kiểm thử và độ tin cậy | `feature/week-06-reliability` | Idempotency, retry, DLQ, outbox, CI | Chưa bắt đầu |
| 7 | Hoàn thiện và bàn giao | `feature/week-07-release` | Đóng gói, tài liệu, demo, bàn giao | Chưa bắt đầu |

## 3. Chi tiết từng tuần

### TUẦN 1 — Phân tích và khởi tạo

**Mục tiêu:** Hiểu đúng bài toán, chốt thiết kế ban đầu, dựng bộ khung chạy được.

| Task | Công việc | Sản phẩm bàn giao | Hoàn thành khi | Trạng thái |
|---|---|---|---|---|
| W1-01 | Phân tích yêu cầu, vai trò, phạm vi | `docs/requirements.md` | Đủ actor, phạm vi in/out, FR, NFR, giả định | Hoàn thành |
| W1-02 | Quy trình nghiệp vụ, quy tắc trạng thái, phân công, quyền | `docs/business-rules.md` | Có luồng, bảng chuyển trạng thái, quy tắc phân công, ma trận quyền | Hoàn thành |
| W1-03 | Use Case, ERD, kiến trúc bằng Mermaid | `docs/architecture.md` | Có 3 sơ đồ Mermaid: Use Case, ERD, kiến trúc | Hoàn thành |
| W1-04 | Khởi tạo solution API, Worker, Contracts, Tests | `SupportTicket.sln`, `src/`, `tests/` | Solution build 0 lỗi; API và Worker khởi động | Hoàn thành |
| W1-05 | Docker Compose cho SQL Server và RabbitMQ | `docker-compose.yml`, `.env.example` | `docker compose ps` báo cả hai healthy; truy vấn SQL và RabbitMQ thành công | Hoàn thành |
| W1-06 | README, `.gitignore`, kế hoạch 7 tuần | `README.md`, `.gitignore`, `docs/plan-7-weeks.md` | README đủ bước chạy; có `.env.example`; kế hoạch đủ 7 tuần | Hoàn thành |

**Nghiệm thu tuần 1:**

- [x] `dotnet build` thành công, 0 warning/0 error.
- [x] `dotnet test` thành công (2 test smoke).
- [x] API khởi động và trả `/health`, `/api/info`.
- [x] Worker khởi động và ghi log nhịp tim.
- [x] SQL Server và RabbitMQ chạy bằng Docker Compose, healthcheck đạt.
- [x] Có tài liệu hướng dẫn chạy và thiết kế ban đầu.
- [x] Có báo cáo `docs/progress/week-01.md`.

### TUẦN 2 — Cơ sở dữ liệu và API

**Mục tiêu:** Lưu trữ và truy xuất ticket an toàn, đúng quyền.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W2-01 | Tạo entity `Users`, `Teams`, `TeamMembers`, `Tickets`, `TicketHistories` | `Domain/Entities` | Đủ 5 entity + quan hệ; solution build thành công |
| W2-02 | DbContext, migration, dữ liệu mẫu | `AppDbContext`, `Migrations/`, seed | Migration áp dụng được; seed đủ 3 role và 4 nhóm |
| W2-03 | Đăng nhập, JWT, phân quyền theo role | `POST /auth/login`, policy | Đăng nhập đúng/sai có kết quả đúng; endpoint được bảo vệ |
| W2-04 | `POST /tickets` | API tạo ticket trạng thái `New` | Ticket lưu `New`, người tạo lấy từ token, trả `201` |
| W2-05 | Danh sách, chi tiết, ticket theo người dùng | `GET /tickets`, `/tickets/{id}`, `/users/{userId}/tickets` | Phân trang/lọc hoạt động; khách hàng chỉ thấy ticket của mình |
| W2-06 | Validation, phân trang, kiểm thử quyền truy cập | Test + tài liệu | Có test validation và test `403/404` khi truy cập chéo |

**Nghiệm thu:**

- [ ] Ticket tạo mới lưu đúng trạng thái `New`, không có nhóm/người xử lý.
- [ ] Truy vấn lại được dữ liệu đã lưu.
- [ ] Dữ liệu không hợp lệ bị từ chối với `400`/`ProblemDetails`.
- [ ] Khách hàng không đọc được ticket của người khác (`403`/`404`).
- [ ] Migration chạy được trên SQL Server trong Docker.

### TUẦN 3 — RabbitMQ và Assignment Worker

**Mục tiêu:** Ticket mới được xử lý nền và tự giao đúng nhóm.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W3-01 | Hợp đồng message: `MessageId`, `TicketId`, `OccurredAt`, `Version` | `SupportTicket.Contracts` | Contract serialize/deserialize được và dùng chung cho API/Worker |
| W3-02 | Cấu hình exchange, queue, routing key | Hằng số + cấu hình | Exchange/queue durable, binding đúng routing key |
| W3-03 | API publish `TicketCreated` sau khi lưu | Event publisher | Publish sau commit; có test xác nhận message xuất hiện |
| W3-04 | Worker nhận message, phân loại theo `Category` | Consumer + rule | 4 danh mục ánh xạ đúng 4 nhóm, mặc định `GENERAL` |
| W3-05 | Lưu nhóm, chuyển `Assigned`, ghi lịch sử | Worker persistence | Ticket `Assigned` + lịch sử trong cùng transaction; chỉ ACK sau khi lưu |
| W3-06 | Kiểm thử luồng API → RabbitMQ → Worker → DB | Integration test + log | Test end-to-end đạt; log có `MessageId`, `TicketId`, kết quả |

**Nghiệm thu:**

- [ ] Ticket `Account` vào `TECH`, `Payment` vào `ACCT`, `Delivery` vào `CSKH`.
- [ ] Danh mục khác/không khớp vào `GENERAL`.
- [ ] Worker chỉ ACK sau khi lưu thành công.
- [ ] Log thể hiện `MessageId`, `TicketId`, kết quả phân công.
- [ ] Có test tích hợp cho luồng chính.

### TUẦN 4 — Trạng thái, phân công và lịch sử

**Mục tiêu:** Hoàn chỉnh vòng đời ticket và kiểm soát quyền chặt chẽ.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W4-01 | `PATCH /tickets/{id}/status` | API chuyển trạng thái | PATCH hoạt động và trả ticket sau cập nhật |
| W4-02 | Chặn chuyển trạng thái sai thứ tự | State machine + `409` | Mọi chuyển sai trả `409`; chuyển đúng trả `200` |
| W4-03 | Kiểm tra quyền người cập nhật | Authorization | Customer/Staff sai quyền nhận `403` |
| W4-04 | Phân công lại nhóm/người theo quyền | `PATCH /tickets/{id}/assignment` | Admin gán lại được; lịch sử ghi thay đổi |
| W4-05 | Lưu và cung cấp API lịch sử | `GET /tickets/{id}/history` | Lịch sử đúng thứ tự và đủ trường cũ/mới |
| W4-06 | Phát sự kiện thay đổi + test nghiệp vụ | Event + test | Test state machine/quyền đạt; event chỉ phát sau commit |

**Nghiệm thu:**

- [ ] Đi hết `New → Assigned → InProgress → Resolved`.
- [ ] Chặn mọi chuyển trạng thái sai và cập nhật sai quyền.
- [ ] Lịch sử phản ánh đúng từng thay đổi, đúng người/thời gian.
- [ ] Sự kiện chỉ phát sau khi dữ liệu commit thành công.
- [ ] Có test cho ma trận chuyển trạng thái và quyền.

### TUẦN 5 — Giao diện và thông báo realtime

**Mục tiêu:** Người dùng thao tác được toàn bộ nghiệp vụ trên web và nhận cập nhật tức thời.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W5-01 | Trang đăng nhập | `wwwroot/login.html` | Đăng nhập lưu token và chuyển trang theo role |
| W5-02 | Form tạo ticket, danh sách ticket | `wwwroot/index.html`, JS | Tạo ticket và thấy ngay trong danh sách |
| W5-03 | Trang chi tiết và lịch sử | `ticket.html` | Chi tiết và timeline lịch sử hiển thị đúng |
| W5-04 | Màn hình nhân viên cập nhật trạng thái | `staff.html` | Staff đổi trạng thái thành công qua UI |
| W5-05 | SignalR Hub + Notification Service | Hub, client JS | Client nhận đúng sự kiện qua group user/team |
| W5-06 | Kết nối lại, tải lại dữ liệu, kiểm thử nhiều người | Test thủ công + tài liệu | Reconnect đồng bộ lại; người ngoài không nhận dữ liệu |

**Nghiệm thu:**

- [ ] Thực hiện được nghiệp vụ chính qua giao diện.
- [ ] Nhân viên đổi trạng thái, khách hàng thấy cập nhật không cần tải lại trang.
- [ ] Người không liên quan không nhận dữ liệu ticket.
- [ ] Sau mất kết nối, client kết nối lại và đồng bộ trạng thái từ API.

### TUẦN 6 — Kiểm thử và độ tin cậy

**Mục tiêu:** Hệ thống chịu được giao lại message, lỗi tạm thời và mất kết nối RabbitMQ.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W6-01 | Chống xử lý trùng | Idempotency check + test | Giao lại message 2 lần không tạo phân công/lịch sử trùng |
| W6-02 | Retry có giới hạn, DLQ | Cấu hình + consumer | Retry N lần rồi vào DLQ; không lặp vô hạn |
| W6-03 | Outbox phát sự kiện | Bảng outbox + publisher job | Tắt RabbitMQ, sự kiện vẫn được gửi lại khi bật lại |
| W6-04 | Kiểm thử Worker/RabbitMQ gián đoạn | Kịch bản + test | Có kịch bản và kết quả ghi lại cho từng tình huống |
| W6-05 | Hoàn thiện integration test, Postman collection | `postman/`, test | Integration test và collection chạy được |
| W6-06 | GitHub Actions build/test trên PR | `.github/workflows/ci.yml` | Workflow chạy build/test thành công trên PR |

**Nghiệm thu:**

- [ ] Giao lại message không tạo phân công/lịch sử trùng.
- [ ] Sự kiện chưa gửi được gửi lại sau khi RabbitMQ phục hồi.
- [ ] Message lỗi vào DLQ, không lặp vô hạn.
- [ ] CI chạy thành công; ghi rõ phần chưa chạy được và lý do.

### TUẦN 7 — Hoàn thiện và bàn giao

**Mục tiêu:** Dự án chạy được từ môi trường sạch và sẵn sàng demo/bảo vệ.

| Task | Công việc | Sản phẩm | Hoàn thành khi |
|---|---|---|---|
| W7-01 | Sửa lỗi, hoàn thiện giao diện | Mã nguồn | Không còn lỗi P1/P2; UI nhất quán |
| W7-02 | Đóng gói API, Worker, giao diện bằng Docker Compose | `docker-compose.yml`, Dockerfile | `docker compose up` chạy cả hệ thống, không cần `dotnet run` |
| W7-03 | Chạy thử theo README từ môi trường sạch | Biên bản kiểm tra | Có biên bản chạy sạch theo đúng README |
| W7-04 | Tài liệu API và báo cáo kiểm thử | `docs/api.md`, `docs/test-report.md` | Tài liệu đủ endpoint, mã lỗi, kết quả test |
| W7-05 | Dữ liệu mẫu, ảnh minh chứng, kịch bản demo | `docs/demo.md`, seed | Có seed, ảnh và kịch bản demo từng chức năng |
| W7-06 | Tổng kết chức năng, hạn chế, hướng phát triển | `docs/summary.md` | Tổng kết đủ chức năng, hạn chế, hướng phát triển |

**Nghiệm thu:**

- [ ] Người khác chạy được dự án theo README.
- [ ] Demo đủ chức năng bắt buộc.
- [ ] Có mã nguồn, tài liệu, kết quả kiểm thử, hướng dẫn bàn giao.
- [ ] Chuẩn bị bản `v1.0.0`; **chỉ tạo tag/release khi có yêu cầu**.

## 4. GitHub: issue, milestone và pull request

### 4.1. Trạng thái công cụ tại thời điểm lập kế hoạch

- Máy đã kiểm tra: **chưa cài `gh` CLI** và repository **chưa có remote**.
- Vì vậy chưa tạo issue/milestone/PR thật. Không có gì được giả lập trong mục này.

### 4.2. Khi đã có repository GitHub

```bash
# Thêm remote (thay URL bằng repository thật)
git remote add origin https://github.com/<tai-khoan>/<ten-repo>.git

# Đẩy nhánh main và nhánh tuần 1
git push -u origin main
git push -u origin feature/week-01-foundation
```

Tạo 7 milestone (web UI: Issues → Milestones) với tên:

1. Tuần 1 — Khởi tạo nền tảng
2. Tuần 2 — Cơ sở dữ liệu và API
3. Tuần 3 — RabbitMQ và Assignment Worker
4. Tuần 4 — Trạng thái, phân công và lịch sử
5. Tuần 5 — Giao diện và thông báo realtime
6. Tuần 6 — Kiểm thử và độ tin cậy
7. Tuần 7 — Hoàn thiện và bàn giao

Tạo issue cho từng task `Wx-yy`, gắn milestone tương ứng. Nếu cài `gh` và đăng nhập:

```bash
gh auth login
gh issue create --title "W1-01 Phân tích yêu cầu" --body "Xem docs/plan-7-weeks.md" --milestone "Tuần 1 — Khởi tạo nền tảng"
```

(Mỗi task lặp lại một lệnh tương tự; có thể viết script sau khi có remote.)

### 4.3. Quy trình PR

1. Tạo nhánh tuần từ `main` mới nhất.
2. Commit theo task, không commit rỗng.
3. Push nhánh và tạo **draft PR** vào `main`.
4. Điền mô tả: phạm vi, cách chạy, kết quả test, ảnh minh chứng.
5. Không tự merge; chờ người dùng/giảng viên review.

## 5. Định nghĩa hoàn thành chung

- [ ] Build và test liên quan đạt.
- [ ] Không commit `.env`, mật khẩu, token, `bin/`, `obj/`.
- [ ] Cập nhật tài liệu và báo cáo tuần.
- [ ] Commit đúng phạm vi, thông điệp rõ ràng.
- [ ] Không sửa lịch sử commit, không force push.
