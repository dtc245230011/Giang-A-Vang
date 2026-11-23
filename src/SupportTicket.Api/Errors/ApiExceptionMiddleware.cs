using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportTicket.Infrastructure.Errors;

namespace SupportTicket.Api.Errors;

/// <summary>Chuẩn hóa mọi lỗi thành ProblemDetails, không lộ stack trace.</summary>
public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException exception)
        {
            await WriteProblemAsync(context, exception.StatusCode, exception.Title, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblemAsync(
                context, StatusCodes.Status409Conflict, "Xung đột dữ liệu",
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã ngắt kết nối.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lỗi không xử lý được cho {Method} {Path}.", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                throw;
            }

            await WriteProblemAsync(
                context, StatusCodes.Status500InternalServerError, "Lỗi hệ thống",
                "Đã xảy ra lỗi trong quá trình xử lý. Vui lòng thử lại sau.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem);
    }
}
