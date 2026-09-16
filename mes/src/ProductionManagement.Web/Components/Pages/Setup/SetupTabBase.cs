using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Web.Services;

namespace ProductionManagement.Web.Components.Pages.Setup;

// 셋업 탭들의 공용 바탕.
//  (1) DB 작업은 Gate로 한 줄로 세운다(DbWorkGate 주석 참고).
//  (2) 서비스 예외를 앱(WPF ViewModel)과 같은 방식의 화면 문구로 바꾼다:
//      검증 오류 = 서비스가 준 문구 그대로 / 권한 없음 = "권한이 없습니다." / 그 밖 = 호출자가 준 안내 문구(원인은 로그로).
public abstract class SetupTabBase : ComponentBase
{
    protected const string UseLabel = "사용";
    protected const string UnuseLabel = "미사용";
    protected static readonly string[] UseOptions = { UseLabel, UnuseLabel };

    [Inject] protected DbWorkGate Gate { get; set; } = default!;
    [Inject] private ILoggerFactory LoggerFactory { get; set; } = default!;

    // 작업을 실행하고, 실패하면 화면에 보여줄 문구를 돌려준다(성공이면 null).
    protected async Task<string?> TryAsync(Func<Task> work, string failMessage)
    {
        try
        {
            await work();
            return null;
        }
        catch (ValidationException ex)
        {
            return string.Join(Environment.NewLine, ex.Errors);
        }
        catch (UnauthorizedException)
        {
            return "권한이 없습니다.";
        }
        catch (Exception ex)
        {
            LoggerFactory.CreateLogger(GetType()).LogError(ex, "셋업 작업 실패: {Message}", failMessage);
            return failMessage;
        }
    }

    protected static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static bool Has(string? source, string keyword)
        => (source ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);

    protected static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    // 표 표시용(값 없으면 "-"), 입력칸 채우기용(값 없으면 빈칸). 소수점 뒤 불필요한 0은 붙이지 않는다.
    protected static string Num(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";

    protected static string? NumInput(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture);
}
