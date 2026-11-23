# API và phân quyền

> Cập nhật: 2026-11-23. Base URL mặc định: `http://localhost:5026` (đổi theo `API_PORT`).
> Mọi endpoint nghiệp vụ hỗ trợ cả đường dẫn gốc và tiền tố `/api`, ví dụ `/tickets` và `/api/tickets`.

## 1. Quy ước chung

- JSON camelCase; enum gửi/nhận dạng chuỗi: `Account`, `Payment`, `Delivery`, `Other`;
  `Low`, `Medium`, `High`; `New`, `Assigned`, `InProgress`, `Resolved`;
  `Customer`, `Agent`, `Admin`.
- Thời gian ISO-8601 UTC (`...Z`).
- Xác thực: `Authorization: Bearer <JWT>`.
- Lỗi trả `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Dữ liệu không hợp lệ",
  "status": 400,
  "detail": "Vui lòng kiểm tra các trường được đánh dấu.",
  "instance": "/api/tickets",
  "traceId": "00-...",
  "errors": { "Title": ["Tiêu đề phải từ 5 đến 200 ký tự."] }
}
```

- Mã lỗi: `400` sai dữ liệu, `401` chưa đăng nhập/sai mật khẩu, `403` không có quyền,
  `404` không tồn tại, `409` xung đột trạng thái/phiên bản, `500` lỗi hệ thống (không lộ stack trace).
- Phân trang: `page ≥ 1`, `pageSize` 1–100, mặc định 20. Kết quả:

```json
{ "items": [], "page": 1, "pageSize": 20, "totalItems": 0, "totalPages": 0 }
```

## 2. Danh sách endpoint

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| `POST` | `/auth/register` | Công khai | Đăng ký, luôn tạo Customer |
| `POST` | `/auth/login` | Công khai | Trả JWT + hồ sơ người dùng |
| `GET` | `/auth/me` | Đã đăng nhập | Hồ sơ hiện tại |
| `POST` | `/auth/logout` | Đã đăng nhập | Client xóa token; API trả 204 |
| `GET` | `/meta` | Công khai | Nhãn danh mục/ưu tiên/trạng thái/vai trò |
| `POST` | `/tickets` | Đã đăng nhập | Tạo ticket **bắt buộc** |
| `GET` | `/tickets` | Đã đăng nhập | Danh sách theo quyền **bắt buộc** |
| `GET` | `/tickets/{id}` | Theo tài nguyên | Chi tiết **bắt buộc** |
| `PATCH` | `/tickets/{id}/status` | Agent phù hợp/Admin | Đổi trạng thái **bắt buộc** |
| `GET` | `/tickets/{id}/history` | Theo tài nguyên | Lịch sử thay đổi |
| `PATCH` | `/tickets/{id}/assignment` | Admin | Phân công/gán lại nhóm, người |
| `GET` | `/users/{userId}/tickets` | Chính mình/Admin | Ticket do người dùng tạo **bắt buộc** |
| `GET` | `/dashboard/summary` | Đã đăng nhập | Số liệu thật theo quyền |
| `GET` | `/teams` | Đã đăng nhập | Danh sách nhóm (chỉ mã/tên/số thành viên) |
| `GET` | `/teams/{id}` | Admin/Agent | Chi tiết nhóm + thành viên (có email) |
| `POST` | `/teams` | Admin | Tạo nhóm |
| `PATCH` | `/teams/{id}` | Admin | Sửa tên/mô tả/vô hiệu hóa nhóm |
| `PUT` | `/teams/{id}/members/{userId}` | Admin | Thêm thành viên/đặt trưởng nhóm |
| `DELETE` | `/teams/{id}/members/{userId}` | Admin | Gỡ thành viên |
| `GET` | `/users` | Admin | Danh sách tài khoản, có lọc |
| `GET` | `/users/agents` | Admin/Agent | Danh sách nhân viên (lọc theo nhóm) |
| `POST` | `/users` | Admin | Tạo tài khoản với vai trò chỉ định |
| `PATCH` | `/users/{id}` | Admin | Đổi họ tên/vai trò/trạng thái/mật khẩu |
| `GET` | `/health` | Công khai | Health check (có kiểm tra SQL Server) |
| `GET` | `/api/info` | Công khai | Thông tin dịch vụ |
| `POST` | `/hubs/notifications/negotiate` | Đã đăng nhập | Thương lượng kết nối SignalR |

### Bộ lọc `GET /tickets`

| Tham số | Kiểu | Ghi chú |
|---|---|---|
| `page`, `pageSize` | int | Mặc định 1 / 20; tối đa 100 |
| `status` | enum | `New`, `Assigned`, `InProgress`, `Resolved` |
| `priority` | enum | `Low`, `Medium`, `High` |
| `category` | enum | `Account`, `Payment`, `Delivery`, `Other` |
| `search` | string | Tìm trong mã ticket hoặc tiêu đề |
| `teamId` | guid | Lọc theo nhóm xử lý |
| `assignedUserId` | guid | Lọc theo nhân viên |
| `unassigned` | bool | Chỉ ticket chưa có nhóm |

## 3. Xác thực

### 3.1. Đăng ký

`POST /auth/register` — trường `role` nếu có trong body bị bỏ qua; tài khoản luôn là `Customer`.

```json
{
  "fullName": "Nguyễn Văn A",
  "email": "a@example.com",
  "password": "MatKhau@123"
}
```

`201 Created`:

```json
{
  "token": "<JWT>",
  "expiresAt": "2026-11-23T17:00:00Z",
  "user": {
    "id": "…",
    "fullName": "Nguyễn Văn A",
    "email": "a@example.com",
    "role": "Customer",
    "isActive": true,
    "createdAt": "2026-11-23T15:00:00Z"
  }
}
```

### 3.2. Đăng nhập

`POST /auth/login` với `{ "email": "...", "password": "..." }`.
Sai thông tin → `401`; tài khoản bị vô hiệu hóa → `403`.

### 3.3. Đăng xuất

JWT là stateless. `POST /auth/logout` trả `204`; giao diện xóa token khỏi `localStorage`.
Mọi request sau đó không còn `Authorization` và nhận `401`.

## 4. Năm API bắt buộc — ví dụ

### 4.1. `POST /tickets`

Quyền: mọi tài khoản đã đăng nhập. `RequesterId` lấy từ JWT, không nhận từ body.
`status` luôn `New`, nhóm/người xử lý luôn rỗng.

```json
{
  "title": "Không đăng nhập được vào tài khoản",
  "description": "Tôi đã đổi mật khẩu hôm qua nhưng hôm nay không đăng nhập được.",
  "category": "Account",
  "priority": "High"
}
```

`201 Created`, header `Location: /api/tickets/{id}`:

```json
{
  "id": "9edfe060-1a4c-42b2-82c6-f779c53434d1",
  "ticketNumber": "TK-261123-UWJ4",
  "title": "Không đăng nhập được vào tài khoản",
  "description": "…",
  "category": "Account",
  "priority": "High",
  "status": "New",
  "requester": { "id": "…", "fullName": "Nguyễn Thị An", "email": "customer@support.local" },
  "assignedTeam": null,
  "assignedUser": null,
  "createdAt": "2026-11-23T15:12:00Z",
  "updatedAt": "2026-11-23T15:12:00Z",
  "resolvedAt": null,
  "rowVersion": "AAAAAAAAB90=",
  "canChangeStatus": false,
  "canAssign": false,
  "allowedNextStatuses": []
}
```

Validation: `title` 5–200, `description` 10–4000 sau khi trim; `category` bắt buộc;
`priority` mặc định `Medium`.

### 4.2. `GET /tickets`

Phạm vi tự động:

| Vai trò | Dữ liệu thấy |
|---|---|
| Customer | Ticket do mình tạo |
| Agent | Ticket do mình tạo + ticket được giao trực tiếp + ticket thuộc nhóm mình |
| Admin | Toàn bộ |

Ví dụ: `GET /api/tickets?status=Assigned&priority=High&category=Account&search=TK-261123&page=1&pageSize=20`.

### 4.3. `GET /tickets/{id}`

- `404`: không tồn tại.
- `403`: tồn tại nhưng không có quyền.
- `rowVersion`: gửi lại khi cập nhật để phát hiện xung đột.
- `allowedNextStatuses`: gợi ý thao tác cho giao diện; backend vẫn kiểm tra lại.

### 4.4. `PATCH /tickets/{id}/status`

Chỉ nhân viên được giao/thuộc nhóm được giao hoặc Admin.

```json
{
  "status": "InProgress",
  "note": "Nhân viên bắt đầu xử lý.",
  "rowVersion": "AAAAAAAAB90="
}
```

- Hợp lệ: `Assigned → InProgress`, `InProgress → Resolved`.
- `New → Assigned` do Worker hoặc Admin qua API phân công.
- Các chuyển khác hoặc chuyển lùi → `409`.
- Trùng `rowVersion` đã cũ → `409`; client cần tải lại.
- `Resolved` ghi `resolvedAt`; không mở lại trong phạm vi đồ án.

### 4.5. `GET /users/{userId}/tickets`

- Customer/Agent chỉ gọi được với `userId` của chính mình; gọi người khác → `403`.
- Admin gọi được cho bất kỳ ai.
- Hỗ trợ cùng bộ lọc/phân trang như `GET /tickets`.

## 5. API phân công và lịch sử

### 5.1. `PATCH /tickets/{id}/assignment` (Admin)

```json
{
  "teamId": "11111111-1111-1111-1111-111111111101",
  "assignedUserId": "22222222-2222-2222-2222-222222222301",
  "note": "Chuyển cho kỹ thuật xử lý.",
  "rowVersion": "AAAAAAAAB90="
}
```

- `teamId` bắt buộc, nhóm phải đang hoạt động.
- `assignedUserId` có thể rỗng; nếu có phải là Agent đang hoạt động và thuộc nhóm.
- Ticket `New` → `Assigned`; ticket `Assigned/InProgress` giữ trạng thái.
- Không phân công ticket `Resolved` (`409`).
- Mỗi thay đổi ghi lịch sử `AssignedTeamId`/`AssignedUserId` và phát sự kiện `TicketAssigned`.

### 5.2. `GET /tickets/{id}/history`

```json
[
  {
    "id": "…",
    "fieldChanged": "Status",
    "oldValue": null,
    "newValue": "New",
    "note": "Khách hàng tạo yêu cầu",
    "changedAt": "2026-11-23T15:12:00Z",
    "changedByUserId": "…",
    "changedByName": "Nguyễn Thị An"
  },
  {
    "id": "…",
    "fieldChanged": "AssignedTeamId",
    "oldValue": null,
    "newValue": "TECH",
    "note": "Hệ thống tự động phân công theo danh mục.",
    "changedAt": "2026-11-23T15:12:01Z",
    "changedByUserId": null,
    "changedByName": null
  }
]
```

`changedByUserId = null` nghĩa là hệ thống/Worker thực hiện.

## 6. Ma trận quyền

| Hành động | Customer | Agent | Admin |
|---|---|---|---|
| Đăng ký công khai | Có (luôn Customer) | — | — |
| Tạo ticket | Có | Có | Có |
| Xem ticket mình tạo | Có | Có | Có |
| Xem ticket được giao/nhóm | Không | Có | Có |
| Xem toàn bộ | Không | Không | Có |
| Xem lịch sử | Theo quyền xem ticket | Theo quyền xem ticket | Có |
| `Assigned → InProgress` | Không | Có nếu được giao | Có |
| `InProgress → Resolved` | Không | Có nếu được giao | Có |
| Phân công lại | Không | Không | Có |
| Quản lý người dùng/nhóm | Không | Không | Có |

## 7. Quy tắc trạng thái và phân công

| Danh mục | Nhóm | Mã |
|---|---|---|
| `Account` | Kỹ thuật | `TECH` |
| `Payment` | Kế toán | `ACCT` |
| `Delivery` | Chăm sóc khách hàng | `CSKH` |
| `Other` / giá trị lạ | Hỗ trợ chung | `GENERAL` |

Worker chọn trưởng nhóm đang hoạt động (hoặc thành viên đầu tiên) làm `AssignedUserId`.
Nếu nhóm chưa có nhân viên hoạt động, ticket vẫn ở cấp nhóm và `AssignedUserId = null`.
Không gán người không hoạt động hoặc không thuộc nhóm.

## 8. SignalR

- URL hub: `/hubs/notifications`.
- Xác thực: JWT qua `access_token` query (client JS tự thêm) hoặc header cho negotiate.
- Hub có `[Authorize]`; server tự thêm group `user:{id}` và các group `team:{teamId}` khi kết nối.
  Client **không** có phương thức join group; không thể đăng ký nhận thay người khác.
- Gửi theo user đã xác định ở server bằng `Clients.Users`; mỗi người nhận tối đa một bản cho mỗi sự kiện.

### Sự kiện

| Tên | Khi nào | Đối tượng nhận |
|---|---|---|
| `TicketAssigned` | Worker/Admin phân công | Người tạo, người được gán, thành viên nhóm; loại trừ người thực hiện |
| `TicketStatusChanged` | Trạng thái đổi | Như trên; loại trừ người thực hiện |

Payload (không có mô tả ticket):

```json
{
  "ticketId": "…",
  "ticketNumber": "TK-261123-UWJ4",
  "title": "Không đăng nhập được vào tài khoản",
  "status": "Assigned",
  "oldStatus": "New",
  "newStatus": "Assigned",
  "category": "Account",
  "teamId": "…",
  "teamName": "Kỹ thuật",
  "assignedUserId": "…",
  "assignedUserName": "Nguyễn Văn Kỹ Thuật",
  "eventType": "TicketAssigned",
  "changedAt": "2026-11-23T15:12:01Z",
  "changedByUserId": null
}
```

`TicketStatusChanged` bổ sung `oldStatus` và `newStatus`; `TicketAssigned` không có hai trường này.
Giao diện hiển thị toast + chuông, tự tải lại danh sách/chi tiết, hiển thị trạng thái kết nối và tự kết nối lại.
SignalR không lưu thông báo khi offline; khi kết nối lại, dữ liệu được tải lại qua REST API.

## 9. Kiểm thử API

- xUnit: `tests/SupportTicket.Tests` (47 test).
- Postman: [postman/SupportTicket.postman_collection.json](../postman/SupportTicket.postman_collection.json).
- E2E realtime: [tools/e2e/realtime-e2e.mjs](../tools/e2e/realtime-e2e.mjs).
- Kết quả: [test-report.md](test-report.md).
