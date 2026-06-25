using System.Text.Json;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "처리되지 않은 예외: {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(ApiResponse.Fail("서버 오류가 발생했습니다."), JsonOpts);
            await ctx.Response.WriteAsync(body);
        }
    }
}
