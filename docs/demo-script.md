# Kịch bản demo

> Mục tiêu: chứng minh website chạy thật từ giao diện → SQL Server → RabbitMQ → Assignment Worker → SignalR.
> Thời lượng gợi ý: 8–12 phút. Mở sẵn 2 cửa sổ trình duyệt (một cửa sổ ẩn danh) để thấy thông báo realtime.

## 0. Chuẩn bị

```bash
docker compose up -d --build
docker compose ps
curl http://localhost:5026/health
```

Chờ các container healthy. Mở:

- Website: <http://localhost:5026>
- RabbitMQ Management: <http://localhost:15672> (tài khoản trong `.env`)
- Ảnh giao diện tham chiếu: [screenshots](screenshots)

Mật khẩu tài khoản demo là `DEMO_PASSWORD` trong `.env` (không đọc/ghi mật khẩu thật vào tài liệu).

## 1. Khách hàng tạo yêu cầu

1. Đăng nhập `customer@support.local` ở cửa sổ thường.
2. Dashboard hiển thị số liệu thật và trạng thái **Đã kết nối**.
3. Vào **Tạo yêu cầu**, nhập:
   - Tiêu đề: “Không đăng nhập được vào tài khoản”
   - Danh mục: **Tài khoản / Đăng nhập**
   - Ưu tiên: **Cao**
   - Nội dung: mô tả sự cố (≥ 10 ký tự).
4. Bấm **Gửi yêu cầu**.
5. Màn hình xác nhận hiện mã ticket `TK-YYMMDD-XXXX`; bấm **Xem chi tiết yêu cầu**.

**Kỳ vọng:** ticket lưu `New`, sau 1–3 giây Worker tự phân công sang nhóm **Kỹ thuật** và nhân viên
**Nguyễn Văn Kỹ Thuật**. Không cần tải lại trang; toast thông báo xuất hiện.

Kiểm tra RabbitMQ:

- Exchange `support-tickets` có message đi qua.
- Queue `assignment-worker` có consumer; `notification-service` có consumer.
- Queue retry/DLQ trống.

## 2. Nhân viên xử lý

1. Mở cửa sổ ẩn danh thứ hai, đăng nhập `agent.tech@support.local`.
2. Vào **Danh sách yêu cầu**, tìm mã ticket vừa tạo.
3. Mở chi tiết, bấm **Bắt đầu xử lý**, nhập ghi chú, xác nhận.

**Kỳ vọng:** trạng thái chuyển **Đang xử lý**; cửa sổ khách hàng tự cập nhật và hiện toast
“Cập nhật trạng thái yêu cầu”; nhân viên thực hiện không nhận lại thông báo của chính mình.

4. Bấm **Đánh dấu hoàn thành**, nhập ghi chú, xác nhận.

**Kỳ vọng:** trạng thái **Đã giải quyết**, có thời điểm hoàn thành; khách hàng nhận thông báo realtime.

## 3. Kiểm tra lịch sử và quyền

1. Ở cửa sổ khách hàng, mở chi tiết ticket.
2. Cuộn tới **Lịch sử thay đổi**: 6 bản ghi theo thứ tự:
   - `New`
   - Nhóm `TECH`
   - Người xử lý
   - `New → Assigned`
   - `Assigned → InProgress`
   - `InProgress → Resolved`
3. Khách hàng không thấy nút cập nhật trạng thái hay phân công.
4. Đăng nhập `customer2@support.local` ở cửa sổ khác: không thấy ticket của customer1, mở trực tiếp URL nhận `403`.

## 4. Quản trị viên

1. Đăng nhập `admin@support.local`.
2. **Tổng quan:** số liệu toàn hệ thống, yêu cầu chưa phân công, biểu đồ danh mục/ưu tiên.
3. **Quản trị hệ thống → Người dùng:** tìm kiếm, lọc vai trò/trạng thái, mở form sửa, thử vô hiệu hóa một tài khoản test.
4. **Nhóm xử lý:** xem 4 nhóm, mở **Thành viên**, thêm/gỡ nhân viên, đặt trưởng nhóm.
5. Mở một ticket `Assigned/InProgress` bất kỳ: khối **Phân công lại** cho phép chọn nhóm và nhân viên thuộc nhóm.
6. Thử phân công ticket `Resolved`: hệ thống chặn với thông báo phù hợp (409).

## 5. Kiểm tra độ tin cậy (tùy chọn, 2–3 phút)

### 5.1. Dừng Worker

```bash
docker compose stop worker
```

Tạo ticket mới ở giao diện → ticket vẫn `New`; RabbitMQ Management thấy message trong `assignment-worker`
với `consumers=0`. Chạy lại:

```bash
docker compose start worker
```

Ticket tự chuyển `Assigned`.

### 5.2. Dừng RabbitMQ

```bash
docker compose stop rabbitmq
```

Tạo ticket mới → API vẫn trả thành công, ticket `New`, Outbox có bản ghi chờ. Khởi động lại:

```bash
docker compose start rabbitmq
```

Sau khi broker healthy, Outbox gửi lại; ticket được Worker phân công. Log API có thể hiện retry tăng dần,
nhưng không mất sự kiện.

## 6. Kết thúc

- Dừng: `docker compose down` (giữ dữ liệu).
- Xóa toàn bộ dữ liệu demo: `docker compose down -v` (cảnh báo: không thể hoàn tác).
- Báo cáo kiểm thử đầy đủ: [test-report.md](test-report.md).
- Checklist chức năng: [completion-checklist.md](completion-checklist.md).
