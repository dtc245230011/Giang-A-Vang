namespace SupportTicket.Infrastructure.Errors;

/// <summary>Lỗi nghiệp vụ có mã HTTP xác định, được middleware chuyển thành ProblemDetails.</summary>
public sealed class AppException(int statusCode, string message, string? title = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title ?? "Không thể thực hiện yêu cầu";

    public static AppException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message, "Dữ liệu không hợp lệ");
    public static AppException Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message, "Chưa xác thực");
    public static AppException Forbidden(string message) => new(StatusCodes.Status403Forbidden, message, "Không có quyền");
    public static AppException NotFound(string message) => new(StatusCodes.Status404NotFound, message, "Không tìm thấy");
    public static AppException Conflict(string message) => new(StatusCodes.Status409Conflict, message, "Xung đột dữ liệu");
}

internal static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status401Unauthorized = 401;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status409Conflict = 409;
}
