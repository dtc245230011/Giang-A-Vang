# W1-02 — Quy trình nghiệp vụ và quy tắc

> Tài liệu này là nguồn tham chiếu cho API, Worker, kiểm thử và giao diện.

## 1. Luồng nghiệp vụ chính

1. Khách hàng đăng nhập và gửi yêu cầu hỗ trợ.
2. Support API xác thực JWT, kiểm tra dữ liệu và lưu ticket trạng thái `New`.
3. API ghi lịch sử tạo ticket trong cùng transaction.
4. API ghi sự kiện `TicketCreated` vào Outbox trong cùng transaction; tiến trình nền gửi lên RabbitMQ.
5. Assignment Worker nhận thông điệp, xác định nhóm theo danh mục.
6. Worker lưu nhóm xử lý, chuyển ticket sang `Assigned` và ghi lịch sử trong cùng transaction.
7. Worker lưu sự kiện `TicketAssigned` vào Outbox trong cùng transaction; tiến trình nền gửi lên RabbitMQ.
8. Notification Service nhận sự kiện và gửi thông báo qua SignalR cho đúng người.
9. Nhân viên được giao chuyển ticket sang `InProgress`, sau đó `Resolved`.
10. Mỗi thay đổi hợp lệ đều lưu lịch sử và phát sự kiện tương ứng.

## 2. Sơ đồ trạng thái

```mermaid
stateDiagram-v2
    [*] --> New: Khách hàng tạo ticket
    New --> Assigned: Worker tự phân công hoặc Admin gán thủ công
    Assigned --> InProgress: Nhân viên được giao bắt đầu xử lý
    InProgress --> Resolved: Nhân viên được giao giải quyết xong
    Resolved --> [*]

    note right of New
        Chưa có nhóm/người xử lý.
        Chờ worker hoặc Admin.
    end note
    note right of Resolved
        Trạng thái kết thúc.
        Không mở lại trong phạm vi đồ án.
    end note
```

## 3. Bảng chuyển trạng thái hợp lệ

| Từ | Sang | Người thực hiện | Điều kiện |
|---|---|---|---|
| `New` | `Assigned` | Worker (tự động) hoặc Admin | Ticket chưa có nhóm; nhóm đích tồn tại |
| `Assigned` | `InProgress` | Agent được gán trực tiếp, Agent thuộc nhóm được giao, Admin | Ticket đang `Assigned` |
| `InProgress` | `Resolved` | Agent được gán trực tiếp, Agent thuộc nhóm được giao, Admin | Ticket đang `InProgress` |

Các chuyển đổi sau bị chặn và trả `409 Conflict`:

- Bỏ qua bước: `New → InProgress`, `New → Resolved`, `Assigned → Resolved`.
- Đi lùi: `InProgress → Assigned`, `Resolved → InProgress`, `Resolved → Assigned`.
- Chuyển sang chính trạng thái hiện tại (coi là không hợp lệ, trừ khi là thao tác phân công lại).
- Khách hàng tự đổi trạng thái (kể cả hủy ticket).

## 4. Quy tắc phân công

Worker chỉ dựa trên `Category` của ticket. Không dùng từ khóa mờ trong phiên bản 1.

| Danh mục | Nhóm xử lý | Mã nhóm |
|---|---|---|
| `Account` (tài khoản, đăng nhập) | Kỹ thuật | `TECH` |
| `Payment` (thanh toán, hóa đơn) | Kế toán | `ACCT` |
| `Delivery` (giao hàng, đổi trả) | Chăm sóc khách hàng | `CSKH` |
| `Other` hoặc giá trị không khớp | Hỗ trợ chung | `GENERAL` |

Quy tắc bổ sung:

- Mức ưu tiên không ảnh hưởng đến việc chọn nhóm trong phạm vi đồ án.
- Worker giao ở cấp nhóm (`AssignedTeamId`) và chọn **trưởng nhóm đang hoạt động** (hoặc thành viên Agent đầu tiên)
  làm `AssignedUserId`. Nếu nhóm chưa có Agent hoạt động, `AssignedUserId = NULL` và ticket vẫn `Assigned`.
- Không gán người không hoạt động, không thuộc nhóm hoặc không phải Agent.
- Admin có thể gán nhóm/người cụ thể bất kỳ lúc nào qua `PATCH /tickets/{id}/assignment`.
- Nếu không tìm thấy nhóm đích trong DB, worker không commit thay đổi; message được retry có giới hạn rồi vào DLQ.
- Phân công lại chỉ Admin thực hiện; ticket `Resolved` không được phân công lại.
- Worker không ghi đè phân công nếu ticket đã có nhóm/người xử lý (message cũ hoặc giao lại).

## 5. Ma trận phân quyền

| Hành động | Khách hàng | Nhân viên (`Agent`) | Admin |
|---|---|---|---|
| Đăng ký công khai | Có, luôn tạo Customer | Không | Không |
| Đăng nhập | Có | Có | Có |
| Tạo ticket | Có | Có | Có |
| Xem ticket do mình tạo | Có | Có | Có |
| Xem ticket được giao cho nhóm/mình | Không | Có | Có |
| Xem toàn bộ ticket | Không | Không | Có |
| Xem lịch sử ticket | Theo quyền xem ticket | Theo quyền xem ticket | Có |
| Chuyển `Assigned → InProgress` | Không | Có, nếu được giao | Có |
| Chuyển `InProgress → Resolved` | Không | Có, nếu được giao | Có |
| Phân công/gán lại nhóm, người | Không | Không | Có |
| Quản lý người dùng, nhóm | Không | Không | Có |

Quy tắc xác định danh tính:

- `CreatedByUserId`, `ChangedByUserId` lấy từ claim trong JWT.
- Client không được gửi `userId` để tự nhận là người khác; nếu có trường này trong body, API bỏ qua.
- `GET /users/{userId}/tickets`: khách hàng chỉ được gọi với chính mình; Admin gọi cho bất kỳ ai;
  nhân viên chỉ nhận kết quả trong phạm vi ticket được phép xem.

## 6. Quy tắc dữ liệu và kiểm tra hợp lệ

### 6.1. Trường bắt buộc khi tạo ticket

| Trường | Quy tắc |
|---|---|
| `title` | Bắt buộc, 5–200 ký tự sau khi trim |
| `description` | Bắt buộc, 10–4000 ký tự sau khi trim |
| `category` | Bắt buộc, một trong `Account`, `Payment`, `Delivery`, `Other` |
| `priority` | Không bắt buộc, mặc định `Medium`; một trong `Low`, `Medium`, `High` |
| `createdByUserId` | Không nhận từ client; lấy từ token |
| `status` | Luôn do server đặt là `New` |
| `assignedTeamId`, `assignedUserId` | Luôn `NULL` khi mới tạo |

### 6.2. Quy tắc chung

- Chuỗi được trim trước khi validate và lưu.
- Không chấp nhận danh mục/trạng thái/ưu tiên ngoài enum; trả `400 Bad Request`.
- Phân trang: `page ≥ 1`, `pageSize` từ 1 đến 100, mặc định 20.
- Không có API xóa ticket trong phạm vi đồ án.
- Ticket đã `Resolved` không được sửa trạng thái hay phân công.
- Cập nhật ticket và ghi lịch sử nằm trong cùng một transaction.
- Dùng `RowVersion` để phát hiện cập nhật đồng thời; xung đột trả `409 Conflict`.
- Lỗi validation trả cấu trúc `ProblemDetails` thống nhất.

## 7. Quy tắc lịch sử

Mỗi thay đổi nghiệp vụ tạo một bản ghi `TicketHistories`:

| Trường | Ý nghĩa |
|---|---|
| `TicketId` | Ticket bị thay đổi |
| `ChangedByUserId` | Người thực hiện; `NULL` nếu hệ thống/worker tự động |
| `FieldChanged` | `Status`, `AssignedTeamId`, `AssignedUserId`, `Priority`, `Category` |
| `OldValue` / `NewValue` | Giá trị trước/sau dạng chuỗi |
| `Note` | Ghi chú, ví dụ "Hệ thống tự động phân công" |
| `ChangedAt` | Thời điểm UTC |

- Ghi một dòng cho mỗi trường thay đổi.
- Lịch sử không được sửa hoặc xóa.
- API trả lịch sử theo thứ tự tăng dần theo `ChangedAt` để hiển thị tiến trình.
- Việc tạo ticket cũng ghi một dòng: `FieldChanged = Status`, `OldValue = NULL`, `NewValue = New`.

## 8. Quy tắc thông báo realtime

- Người nhận được xác định ở server từ quan hệ của ticket, không lấy từ client.
- Nhóm nhận:
  - Người tạo ticket.
  - Nhân viên được gán trực tiếp (nếu có).
  - Toàn bộ thành viên nhóm được giao (nếu có).
- Loại trừ người vừa thực hiện thay đổi để tránh thông báo thừa.
- Admin không nhận thông báo đẩy mặc định; Admin vẫn xem được dữ liệu qua API/giao diện.
- SignalR dùng group theo người (`user:{userId}`) và theo nhóm (`team:{teamId}`), gửi một lần cho mỗi kết nối sau khi hợp nhất danh sách.
- Nội dung thông báo chỉ gồm: `ticketId`, `ticketNumber`, `title`, `status`, `teamName`, `changedAt`;
  không gửi toàn bộ mô tả hay dữ liệu nhạy cảm.
- Nếu gửi thông báo lỗi, nghiệp vụ chính vẫn thành công; lỗi được ghi log để xử lý lại.

## 9. Sự kiện và hàng đợi (đã triển khai)

| Sự kiện | Routing key | Nội dung cốt lõi |
|---|---|---|
| `TicketCreated` | `ticket.created` | `messageId`, `version`, `ticketId`, `ticketNumber`, `category`, `priority`, `requesterId`, `occurredAt` |
| `TicketAssigned` | `ticket.assigned` | `messageId`, `ticketId`, `ticketNumber`, `teamId/teamName`, `assignedUserId/assignedUserName?`, `oldStatus`, `newStatus`, `changedByUserId`, `occurredAt` |
| `TicketStatusChanged` | `ticket.status-changed` | `messageId`, `ticketId`, `ticketNumber`, `oldStatus`, `newStatus`, `changedByUserId`, `team`, `assignedUser`, `occurredAt` |

- Exchange: `support-tickets` kiểu `topic`, durable; message persistent, publisher confirm, `mandatory=true`.
- Outbox: sự kiện lưu cùng transaction với ticket/lịch sử; tiến trình nền gửi lại khi RabbitMQ gián đoạn.
- Queue chính: `assignment-worker` (bind `ticket.created`), `notification-service` (bind `ticket.assigned`, `ticket.status-changed`).
- Retry: `assignment-worker.retry`, `notification-service.retry` — TTL 5 giây, sau đó quay lại queue chính.
- DLQ: `assignment-worker.dlq`, `notification-service.dlq` qua exchange `support-tickets-dlx`; tối đa 3 lần thử.
- Inbox: bảng `InboxMessages` chống xử lý trùng theo `MessageId` ở cả worker và notification consumer.
- Message có `MessageId`; worker chỉ ACK sau khi transaction commit thành công.

## 10. Trường hợp biên và cách xử lý

| Tình huống | Cách xử lý dự kiến |
|---|---|
| Worker đang tắt | Ticket vẫn ở `New`; message chờ trong queue; Worker chạy lại sẽ xử lý; Admin có thể gán thủ công |
| RabbitMQ tạm mất kết nối khi tạo ticket | Ticket đã lưu vẫn hợp lệ; Outbox giữ sự kiện và gửi lại khi broker phục hồi |
| Thông điệp bị giao lại | Worker kiểm tra ticket đã `Assigned` thì bỏ qua, không ghi lịch sử trùng |
| Hai nhân viên cập nhật cùng lúc | `RowVersion` phát hiện xung đột, một request nhận `409` |
| Nhóm đích không tồn tại | Worker ghi log lỗi, không cập nhật ticket, để retry |
| Người dùng bị khóa | Không đăng nhập được; token cũ hết hạn theo thời gian sống ngắn |
| Danh mục lạ | API từ chối `400`; worker chỉ nhận dữ liệu đã hợp lệ |
| Không có người/nhóm khi `New` | Hợp lệ; đây là trạng thái chờ phân công |
| Gửi thông báo thất bại | Nghiệp vụ vẫn commit; log lỗi; có thể phát lại sự kiện |

## 10.1. Đăng ký công khai

- Chỉ endpoint `POST /auth/register`; không yêu cầu đăng nhập.
- `fullName` 2–150, `email` hợp lệ và chưa tồn tại, `password` 8–100.
- Mọi trường `role` client gửi lên bị bỏ qua; tài khoản mới luôn là `Customer`.
- Đăng ký thành công trả JWT để vào thẳng giao diện; Admin cấp quyền Agent/Admin sau.

## 10.2. Vô hiệu hóa tài khoản và nhóm

- Không có API xóa người dùng/nhóm/ticket; dùng `IsActive = false`.
- Tài khoản bị vô hiệu hóa không đăng nhập được; token cũ bị từ chối ở request kế tiếp.
- Worker và Admin không được gán người/nhóm đang bị vô hiệu hóa.
- Ticket, lịch sử và thông báo cũ vẫn giữ nguyên; Admin có thể phân công lại nếu cần.

## 11. Tiêu chí nghiệm thu nghiệp vụ

1. Không thể tạo ticket thiếu trường bắt buộc hoặc sai enum.
2. Ticket mới luôn ở `New`, không có nhóm/người xử lý.
3. Đăng ký công khai không thể tự tạo quyền Agent/Admin.
4. Admin vô hiệu hóa tài khoản thay vì xóa; dữ liệu lịch sử còn nguyên.
3. Worker giao đúng 4 nhóm theo bảng phân công, kể cả nhánh mặc định `GENERAL`.
4. Mọi chuyển trạng thái sai thứ tự bị chặn với `409`.
5. Khách hàng không đọc hoặc cập nhật được ticket của người khác.
6. Mỗi thay đổi hợp lệ có đúng một bản ghi lịch sử tương ứng.
7. Thông báo chỉ đến người tạo, người được gán và thành viên nhóm xử lý.
