using CleanPotal.Core.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 모든 컨트롤러 응답을 표준 봉투 { success, data, error }로 감싼다.
/// - 2xx + 본문 → success:true, data:본문
/// - 4xx/5xx + 본문 → success:false, error:추출한 메시지
/// - 204(NoContent) / FileResult 등은 그대로 둔다.
/// </summary>
public class EnvelopeResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult obj)
        {
            int status = obj.StatusCode ?? 200;
            if (obj.Value is ApiResponse) { await next(); return; }   // 이미 봉투면 통과

            if (status is >= 200 and < 300)
            {
                obj.Value = ApiResponse.Ok(obj.Value);
            }
            else
            {
                obj.Value = ApiResponse.Fail(ExtractError(obj.Value, status));
            }
        }
        else if (context.Result is StatusCodeResult sc && sc.StatusCode >= 400)
        {
            context.Result = new ObjectResult(ApiResponse.Fail($"요청 실패 ({sc.StatusCode})")) { StatusCode = sc.StatusCode };
        }
        await next();
    }

    private static string ExtractError(object? value, int status)
    {
        if (value is null) return $"요청 실패 ({status})";
        // { error = "..." } 형태에서 메시지 추출
        var prop = value.GetType().GetProperty("error") ?? value.GetType().GetProperty("Error");
        if (prop?.GetValue(value) is string s && !string.IsNullOrEmpty(s)) return s;
        return value as string ?? $"요청 실패 ({status})";
    }
}
