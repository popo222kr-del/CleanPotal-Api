using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.Exceptions;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 셋업(마스터 관리) 컨트롤러 공통.
///
/// 마스터 저장은 실패 사유가 곧 사용자가 고칠 정보인 경우가 많다(중복 코드·권한 없음·규칙 위반).
/// 그런 것은 그대로 보여 주고, 그 밖의 예외만 원문을 감춘다. 실패도 정상적인 답이라 전부 200 이다
/// — 화면이 사유를 읽어 그 자리에 띄운다.
/// </summary>
public abstract class MesSetupControllerBase : ControllerBase
{
    private readonly ILogger _log;

    protected MesSetupControllerBase(ILogger log) => _log = log;

    protected async Task<ActionResult<MesSetupResultDto>> RunAsync(Func<Task> work, string okMessage, string label)
        => Ok(await ExecAsync(work, okMessage, label));

    /// <summary>결과에 개수를 담아 알려 주는 경우(복사 등). 몇 건이 옮겨졌는지가 사용자가 알고 싶은 것이다.</summary>
    protected async Task<ActionResult<MesSetupResultDto>> RunCountAsync(
        Func<Task<int>> work, Func<int, string> okMessage, string label)
    {
        var count = 0;
        var result = await ExecAsync(async () => count = await work(), "", label);
        return Ok(result.Success ? result with { Message = okMessage(count) } : result);
    }

    private async Task<MesSetupResultDto> ExecAsync(Func<Task> work, string okMessage, string label)
    {
        try
        {
            await work();
            return new MesSetupResultDto(true, okMessage);
        }
        catch (UnauthorizedException ex)
        {
            return new MesSetupResultDto(false, ex.Message);
        }
        catch (ValidationException ex)
        {
            return new MesSetupResultDto(false, string.Join(" / ", ex.Errors));
        }
        catch (InvalidOperationException ex)
        {
            return new MesSetupResultDto(false, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES {Label} 실패", label);
            return new MesSetupResultDto(false, $"{label} 중 문제가 발생했습니다. 관리자에게 문의하세요.");
        }
    }
}
