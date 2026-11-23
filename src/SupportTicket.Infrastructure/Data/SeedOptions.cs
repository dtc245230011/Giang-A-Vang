namespace SupportTicket.Infrastructure.Data;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Mật khẩu dùng chung cho các tài khoản demo. Nếu trống, hệ thống sinh mật khẩu ngẫu nhiên và ghi cảnh báo.</summary>
    public string? DefaultPassword { get; set; }

    /// <summary>Cho phép tạo dữ liệu demo (ticket mẫu). Mặc định bật.</summary>
    public bool IncludeDemoTickets { get; set; } = true;
}
