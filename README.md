# Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ khách hàng

Đồ án xây dựng hệ thống tiếp nhận ticket hỗ trợ khách hàng theo kiến trúc hướng sự kiện:
ASP.NET Core Web API, .NET Worker Service, RabbitMQ, SQL Server, SignalR và giao diện web HTML/CSS/JavaScript.

> **Trạng thái:** Tuần 1/7 — đã có bộ khung solution, hạ tầng Docker và tài liệu thiết kế.
> Chức năng nghiệp vụ sẽ được bổ sung từ tuần 2. Không có chức năng AI trong dự án.

## 1. Công nghệ và phiên bản

| Thành phần | Phiên bản / image | Ghi chú |
|---|---|---|
| .NET SDK | 10.0.403 | Cài sẵn trên máy phát triển |
| ASP.NET Core / .NET Runtime | 10.0.14 (`net10.0`) | Bản LTS |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-latest` | Developer Edition, chạy trong Docker |
| RabbitMQ | `rabbitmq:4-management` | Có giao diện quản trị |
| EF Core | 10.x (bổ sung tuần 2) | SQL Server provider |
| SignalR | Có sẵn trong ASP.NET Core | Bổ sung tuần 5 |
| xUnit | 2.9.3 | Kiểm thử |
| Docker / Compose | Docker 29.9.2 / Compose v5.6.1 | Linux containers |
| Giao diện | HTML, CSS, JavaScript thuần | Bổ sung tuần 5 |
| CI | GitHub Actions | Bổ sung tuần 6 |

## 2. Cấu trúc thư mục

```text
.
├── SupportTicket.sln
├── docker-compose.yml
├── .env.example
├── .gitignore
├── README.md
├── docs/
│   ├── requirements.md          # W1-01
│   ├── business-rules.md        # W1-02
│   ├── architecture.md          # W1-03 (Use Case, ERD, kiến trúc)
│   ├── plan-7-weeks.md          # W1-06
│   └── progress/
│       └── week-01.md
├── src/
│   ├── SupportTicket.Api/       # ASP.NET Core Web API + SignalR (tuần 5)
│   ├── SupportTicket.Worker/    # Assignment Worker
│   └── SupportTicket.Contracts/ # Hợp đồng message dùng chung (tuần 3)
└── tests/
    └── SupportTicket.Tests/     # xUnit + kiểm thử tích hợp API
```

## 3. Yêu cầu môi trường

- .NET 10 SDK.
- Docker Desktop (Linux containers) đang chạy.
- Git.
- RAM trống tối thiểu khoảng 4 GB cho SQL Server + RabbitMQ + API/Worker.
- Các cổng còn trống: `1433` (SQL Server), `5672` (AMQP), `15672` (RabbitMQ UI), `5026` (API).

## 4. Chạy hạ tầng bằng Docker Compose

```bash
# 1. Tạo file môi trường cục bộ
cp .env.example .env

# 2. Mở .env và đổi mật khẩu thành giá trị mạnh của bạn
#    (file .env đã được gitignore, không commit)

# 3. Khởi động SQL Server và RabbitMQ
docker compose up -d

# 4. Kiểm tra trạng thái, chờ đến khi cả hai là "healthy"
docker compose ps
```

Thông tin truy cập mặc định (lấy theo `.env`):

| Dịch vụ | Địa chỉ | Ghi chú |
|---|---|---|
| SQL Server | `localhost:1433` | User `sa`, mật khẩu trong `.env` |
| RabbitMQ AMQP | `localhost:5672` | User/mật khẩu trong `.env` |
| RabbitMQ Management | <http://localhost:15672> | Đăng nhập bằng user/mật khẩu trong `.env` |

Dừng hạ tầng:

```bash
docker compose down          # giữ dữ liệu trong volume
docker compose down -v       # xóa cả dữ liệu (chỉ dùng khi muốn làm lại từ đầu)
```

> **Lưu ý:** lần chạy đầu tiên sẽ tải image SQL Server (~1.5 GB) và RabbitMQ, có thể mất vài phút.

## 5. Build, test và chạy dịch vụ

```bash
dotnet restore
dotnet build
dotnet test
```

### Chạy API

```bash
dotnet run --project src/SupportTicket.Api
```

Kiểm tra:

- <http://localhost:5026/health> → `Healthy`
- <http://localhost:5026/api/info> → thông tin dịch vụ dạng JSON
- <http://localhost:5026/openapi/v1.json> → OpenAPI (môi trường Development)

### Chạy Worker

```bash
dotnet run --project src/SupportTicket.Worker
```

Worker ghi log khởi động và nhịp tim mỗi 30 giây (`Worker:HeartbeatSeconds`).
Chức năng nhận message và phân công sẽ được bổ sung ở tuần 3.

### Chạy cả hai (hai terminal)

```bash
# Terminal 1
dotnet run --project src/SupportTicket.Api

# Terminal 2
dotnet run --project src/SupportTicket.Worker
```

## 6. Cấu hình

- `.env`: chỉ dùng cho Docker Compose, đã gitignore.
- API/Worker đọc cấu hình từ `appsettings.json` và biến môi trường.
- Các biến dự kiến cho tuần 2–3 (đặt qua biến môi trường, không commit):

```bash
ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=SupportTicketDb;User Id=sa;Password=<mat-khau>;TrustServerCertificate=True;Encrypt=False"
RabbitMq__Host=localhost
RabbitMq__Port=5672
RabbitMq__UserName=<user>
RabbitMq__Password=<password>
Jwt__Key=<khoa-bi-mat-dai>
```

Không commit mật khẩu, token, khóa JWT hoặc file `.env`.

## 7. API hiện có (tuần 1)

| Method | Endpoint | Mô tả |
|---|---|---|
| `GET` | `/health` | Health check cơ bản |
| `GET` | `/api/info` | Tên dịch vụ, phiên bản, môi trường, thời gian UTC |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development) |

API nghiệp vụ (`/auth/login`, `/tickets`, …) được bổ sung từ tuần 2.

## 8. Kiểm thử

```bash
dotnet test
```

Tuần 1 có 2 test smoke:

- `/health` trả `200` và nội dung `Healthy`.
- `/api/info` trả đúng tên dịch vụ và trạng thái.

Postman collection và kiểm thử tích hợp đầy đủ sẽ được bổ sung ở tuần 6.

## 9. Tài liệu

- [Yêu cầu hệ thống](docs/requirements.md)
- [Quy tắc nghiệp vụ](docs/business-rules.md)
- [Use Case, ERD, kiến trúc](docs/architecture.md)
- [Kế hoạch 7 tuần](docs/plan-7-weeks.md)
- [Báo cáo tuần 1](docs/progress/week-01.md)

## 10. Lộ trình

| Tuần | Nội dung | Trạng thái |
|---|---|---|
| 1 | Phân tích, thiết kế, solution, Docker, tài liệu | Hoàn thành |
| 2 | Entity, migration, đăng nhập, API ticket | Chưa bắt đầu |
| 3 | RabbitMQ, Assignment Worker | Chưa bắt đầu |
| 4 | Trạng thái, phân công, lịch sử | Chưa bắt đầu |
| 5 | Giao diện, SignalR | Chưa bắt đầu |
| 6 | Idempotency, retry, DLQ, outbox, CI | Chưa bắt đầu |
| 7 | Đóng gói, kiểm tra, demo, bàn giao | Chưa bắt đầu |

## 11. Xử lý sự cố thường gặp

| Lỗi | Cách xử lý |
|---|---|
| `failed to connect to the docker API` | Mở Docker Desktop, chờ daemon sẵn sàng |
| SQL Server không khởi động | Kiểm tra RAM; mật khẩu phải đủ mạnh (≥ 8 ký tự, 3/4 nhóm ký tự) |
| `port is already allocated` | Đổi cổng trong `.env` hoặc tắt dịch vụ đang chiếm cổng |
| RabbitMQ UI không mở | Chờ container `healthy`, kiểm tra cổng `15672` |
| Worker không thấy message | RabbitMQ/Worker chưa tích hợp ở tuần 1; chức năng có từ tuần 3 |

## 12. Ghi chú về Git

- Thư mục dự án có repository Git **riêng**, tách khỏi repository ở thư mục người dùng.
- Repository chưa cấu hình remote GitHub. Sau khi có URL repository, thực hiện:

```bash
git remote add origin <repo-url>
git push -u origin main
git push -u origin feature/week-01-foundation
```

Quy trình nhánh và commit được mô tả trong [kế hoạch 7 tuần](docs/plan-7-weeks.md).
