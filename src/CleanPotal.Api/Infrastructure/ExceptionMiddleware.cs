using System.Text.Json;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;

namespace CleanPotal.Api.Infrastructure;

/// <summary>전역 예외 처리 — 서버 크래시를 안전한 표준 봉투 에러로 변환.</summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (BusinessRuleException ex)
        {
            // 사용자가 고칠 수 있는 입력/업무 규칙 오류 → 400 + 실제 메시지 그대로 전달.
            // (서버 버그가 아니므로 Error 가 아니라 Information 으로 남긴다)
            _logger.LogInformation("잘못된 요청: {Path} — {Message}", ctx.Request.Path, ex.Message);
            await WriteAsync(ctx, 400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "처리되지 않은 예외: {Path}", ctx.Request.Path);
            // 내부 오류 메시지는 노출하지 않는다.
            await WriteAsync(ctx, 500, "서버 오류가 발생했습니다.");
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, string message)
    {
        if (ctx.Response.HasStarted) return;   // 이미 응답이 나가기 시작했으면 건드리지 않는다
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse.Fail(message), JsonOpts));
    }
}
