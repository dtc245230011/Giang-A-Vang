# W1-01 — Phân tích yêu cầu hệ thống

> Dự án: Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ khách hàng  
> Phiên bản tài liệu: 1.0 — 2026-11-19  
> Trạng thái: Đã hoàn thành ở tuần 1

## 1. Bối cảnh và mục tiêu

Khách hàng gửi yêu cầu hỗ trợ (ticket) qua giao diện web. Hệ thống phải tiếp nhận, lưu trữ,
phân loại và tự động chuyển ticket đến đúng nhóm xử lý, đồng thời thông báo thay đổi trạng thái
cho người dùng theo thời gian thực.

Mục tiêu của đồ án:

1. Xây dựng luồng nghiệp vụ khép kín từ tạo ticket đến khi ticket được giải quyết.
2. Áp dụng kiến trúc hướng sự kiện với RabbitMQ và một worker xử lý nền.
3. Bảo đảm dữ liệu nhất quán, phân quyền ở backend và thông báo realtime đúng người nhận.
4. Có kiểm thử, CI và tài liệu đủ để người khác chạy lại dự án.

## 2. Phạm vi

### 2.1. Trong phạm vi

- Đăng nhập, xác thực và phân quyền theo vai trò.
- Tạo, tra cứu danh sách và xem chi tiết ticket.
- Đưa sự kiện ticket mới vào RabbitMQ để xử lý nền.
- Worker tự phân loại ticket và giao cho nhóm xử lý theo quy tắc.
- Quản lý trạng thái `New → Assigned → InProgress → Resolved`.
- Phân công lại người/nhóm xử lý theo quyền.
- Lưu lịch sử thay đổi của ticket.
- Phát sự kiện và gửi thông báo realtime qua SignalR.
- Giao diện web HTML/CSS/JavaScript.
- Đóng gói SQL Server, RabbitMQ bằng Docker Compose.
- Kiểm thử đơn vị, kiểm thử tích hợp, Postman và GitHub Actions.

### 2.2. Ngoài phạm vi

- Không bổ sung chức năng AI (theo yêu cầu đề tài).
- Không có ứng dụng mobile, email/SMS, tổng đài.
- Không có đính kèm tệp, chat nội bộ, SLA nâng cao, báo cáo phân tích.
- Không hỗ trợ đa tổ chức (multi-tenant), đăng ký tài khoản tự do.
- Không có chức năng mở lại ticket đã `Resolved`.
- Không triển khai Kubernetes hay hạ tầng cloud.

## 3. Tác nhân và quyền

| Tác nhân | Mô tả | Quyền chính |
|---|---|---|
| Khách hàng (`Customer`) | Người gửi yêu cầu hỗ trợ | Tạo ticket; xem ticket do mình tạo; xem lịch sử ticket của mình; nhận thông báo |
| Nhân viên (`Staff`) | Thành viên nhóm xử lý | Xem ticket được giao cho mình/nhóm; chuyển trạng thái ticket được giao; nhận thông báo |
| Quản trị viên (`Admin`) | Quản lý hệ thống | Xem toàn bộ ticket; phân công lại; đổi trạng thái; xem lịch sử; quản lý người dùng/nhóm |

Chi tiết ma trận quyền nằm trong [business-rules.md](business-rules.md).

## 4. Yêu cầu chức năng

| Mã | Yêu cầu | Ưu tiên | Tiêu chí chấp nhận tóm tắt |
|---|---|---|---|
| FR-01 | Đăng nhập và phân quyền | Bắt buộc | Đăng nhập đúng trả JWT; sai thông tin trả 401; API kiểm tra role |
| FR-02 | Tạo ticket | Bắt buộc | Ticket lưu trạng thái `New`, có người gửi lấy từ token |
| FR-03 | Danh sách ticket | Bắt buộc | Có phân trang, lọc theo trạng thái/ưu tiên; chỉ trả dữ liệu được phép xem |
| FR-04 | Chi tiết ticket | Bắt buộc | Trả đủ trường; 403/404 nếu không có quyền hoặc không tồn tại |
| FR-05 | Ticket theo người dùng | Bắt buộc | `GET /users/{userId}/tickets`; khách hàng chỉ xem của mình |
| FR-06 | Xếp hàng và phân công tự động | Bắt buộc | Ticket mới được đưa vào RabbitMQ; worker giao đúng nhóm theo danh mục |
| FR-07 | Cập nhật trạng thái | Bắt buộc | Chỉ chuyển trạng thái hợp lệ, đúng người có quyền |
| FR-08 | Phân công lại | Bắt buộc | Chỉ Admin; lưu lịch sử; phát sự kiện |
| FR-09 | Lịch sử thay đổi | Bắt buộc | Mỗi thay đổi có bản ghi cũ/mới, người thực hiện, thời gian |
| FR-10 | Thông báo realtime | Bắt buộc | Người có quyền xem nhận thông báo qua SignalR; không gửi nhầm người |
| FR-11 | Kiểm soát truy cập | Bắt buộc | Không tin `userId` từ client; kiểm tra quyền ở backend cho mọi API |
| FR-12 | Độ tin cậy thông điệp | Bắt buộc | Chống trùng, retry có giới hạn, DLQ, outbox (tuần 6) |

## 5. Yêu cầu phi chức năng

### 5.1. Bảo mật

- Mật khẩu lưu dưới dạng hash (ASP.NET Core `PasswordHasher<T>`), không lưu plaintext.
- Xác thực bằng JWT Bearer; khóa ký lấy từ biến môi trường/user-secrets, không commit.
- Danh tính người dùng lấy từ token (`sub`/`NameIdentifier`), không lấy từ body.
- Mọi API đều kiểm tra quyền ở server; giao diện chỉ hỗ trợ trải nghiệm, không phải nơi bảo vệ dữ liệu.
- Không trả về hash mật khẩu hoặc thông tin nội bộ trong response.
- CORS giới hạn theo môi trường; production bắt buộc HTTPS.

### 5.2. Độ tin cậy và nhất quán

- Cập nhật ticket và ghi lịch sử trong cùng một transaction.
- Chỉ publish sự kiện sau khi dữ liệu đã commit thành công.
- Tuần 6 bổ sung Outbox để không mất sự kiện khi RabbitMQ gián đoạn.
- Consumer xử lý trùng không tạo thêm phân công/lịch sử.
- Retry có giới hạn, sau đó đưa vào hàng đợi lỗi (DLQ) và ghi log.
- Dùng optimistic concurrency (`RowVersion`) để tránh ghi đè khi hai người cập nhật cùng lúc.

### 5.3. Hiệu năng

- Danh sách ticket mặc định 20 bản ghi/trang, tối đa 100.
- Các truy vấn chính có index phù hợp (trạng thái, nhóm, người tạo, thời gian).
- Mục tiêu phản hồi API đọc dưới 1 giây với 10.000 ticket trong môi trường demo.

### 5.4. Khả năng quan sát

- Log có cấu trúc, kèm `TicketId`, `MessageId`, `CorrelationId` khi xử lý nền.
- Worker ghi log nhận thông điệp, kết quả phân công, lỗi và số lần retry.

### 5.5. Khả năng bảo trì

- Tách API, Worker, Contracts và Tests thành project riêng.
- Có kiểm thử đơn vị cho quy tắc nghiệp vụ và kiểm thử tích hợp cho luồng chính.
- CI build/test trên pull request.

## 6. Giả định thiết kế

| Mã | Giả định | Lý do |
|---|---|---|
| A-01 | Dùng .NET 10 (LTS), ASP.NET Core, EF Core | SDK 10.0.403 có sẵn trên máy; là bản LTS ổn định |
| A-02 | SQL Server 2022 Developer trong Docker | Miễn phí cho phát triển, tương thích EF Core |
| A-03 | RabbitMQ 4 (management) trong Docker | Có giao diện quản trị tại cổng 15672 |
| A-04 | Xác thực JWT, không dùng cookie/session | Phù hợp API + SPA thuần HTML/JS |
| A-05 | Danh mục cố định: `Account`, `Payment`, `Delivery`, `Other` | Đủ cho 4 quy tắc phân công của đề |
| A-06 | Mức ưu tiên: `Low`, `Medium`, `High`; mặc định `Medium` | Đơn giản, dễ kiểm thử |
| A-07 | `TicketNumber` dạng `TK-XXXXXXXX` (8 ký tự GUID), có unique index | Không phụ thuộc sequence; có thể đổi ở tuần 2 |
| A-08 | Thời gian lưu UTC, hiển thị theo múi giờ trình duyệt | Tránh sai lệch giữa các thành phần |
| A-09 | Tài khoản do Admin tạo/seed dữ liệu mẫu; không tự đăng ký | Giảm phạm vi, phù hợp đồ án |
| A-10 | Notification Service và SignalR Hub chạy cùng tiến trình API | Đơn giản hóa triển khai; worker vẫn chạy riêng |
| A-11 | Một khách hàng có thể là nhân viên ở nhóm khác; quyền xem vẫn theo vai trò | Ma trận quyền xử lý theo role + quan hệ phân công |

## 7. Ràng buộc

- Thời gian thực hiện: 7 tuần.
- Không thêm chức năng AI.
- Chạy được trên máy cá nhân với Docker Desktop.
- Không commit mật khẩu, token, file `.env`, thư mục `bin/`, `obj/`.

## 8. Tiêu chí nghiệm thu tổng thể

1. Người dùng đăng nhập và chỉ thấy dữ liệu thuộc quyền của mình.
2. Tạo ticket thành công, ticket ở trạng thái `New` và được worker tự giao nhóm.
3. Toàn bộ vòng đời `New → Assigned → InProgress → Resolved` hoạt động qua API và giao diện.
4. Mọi thay đổi trạng thái/phân công đều có lịch sử và thông báo realtime.
5. Thông điệp giao lại không tạo dữ liệu trùng; lỗi được retry và đưa vào DLQ.
6. Dự án chạy được từ môi trường sạch theo README; CI build/test thành công.
