using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES Dash Board — MES 를 누르면 처음 보이는 화면.
/// 집계는 MES 업무 계층(ILotService)이 그대로 하고, 여기서는 화면이 쓸 모양으로만 묶는다.
/// </summary>
[ApiController]
[Route("api/mes/dashboard")]
[Authorize(Policy = "ViewMes")]
public class MesDashboardController : ControllerBase
{
    // 전산등록은 공정이 아니라 입구라 공정별 WIP 에서 뺀다(MES 화면과 같은 규칙).
    private const int RegistrationOperCode = 1000;
    // "출하 완료"는 실제 공정이 아니라 당일 출하 건수를 보여주는 맨 끝 카드다. 전용 코드를 쓴다.
    private const int ShippedDoneOperCode = 8200;

    private readonly ILotService _lots;
    public MesDashboardController(ILotService lots) => _lots = lots;

    /// <summary>KPI 와 공정별 WIP. 화면이 둘을 같이 그리므로 한 번에 내려준다.</summary>
    [HttpGet]
    public async Task<ActionResult<MesDashboardDto>> Get(CancellationToken ct)
    {
        var summary = await _lots.GetDashboardSummaryAsync(ct);
        var wip = await _lots.GetProcessWipCountsAsync(ct);

        // 카드 목록을 만드는 규칙은 화면이 아니라 여기 둔다 — 화면이 여럿 되어도 한 군데만 고치면 된다.
        var stages = wip.Where(s => s.OperCode != RegistrationOperCode).ToList();
        stages.Add(new ProcessWipDto(0, "출하 완료", ShippedDoneOperCode, summary.TodayShipped, null, false));

        return Ok(new MesDashboardDto(summary, stages, ShippedDoneOperCode));
    }

    /// <summary>KPI·공정 카드를 눌렀을 때 아래에 펼쳐지는 제품 목록.</summary>
    [HttpGet("lots")]
    public async Task<ActionResult<IReadOnlyList<OperLotItemDto>>> Lots(
        [FromQuery] DashboardLotCategory category, [FromQuery] int? operCode, CancellationToken ct)
        => Ok(await _lots.GetDashboardLotsAsync(category, operCode, ct));
}

/// <summary><paramref name="ShippedDoneOperCode"/>: "출하 완료" 카드를 알아보는 코드.
/// 화면이 이 숫자를 직접 박아 두지 않도록 서버가 같이 내려준다.</summary>
public record MesDashboardDto(
    DashboardSummaryDto Summary,
    IReadOnlyList<ProcessWipDto> Stages,
    int ShippedDoneOperCode);
