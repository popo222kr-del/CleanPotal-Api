using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES LOT 조회 API — MES 화면을 React 로 옮기는 첫 화면("LOT 현황 조회")이 쓴다.
///
/// 조회 로직은 MES 업무 계층(ILotHistoryService)을 그대로 부른다. 같은 규칙을 포털에 다시 구현하면
/// 두 곳이 갈라지기 때문이다.
///
/// 권한: 당장은 현장(field) 영역을 쓴다. MES 는 현장 생산 작업이라 현장 권한을 가진 사람이 곧 대상이고,
/// 지금 새 영역을 만들면 관리자가 등급을 넣어 줄 때까지 아무도 못 들어간다. MES 화면이 다 옮겨온 뒤
/// 전용 영역(mes)으로 분리한다 — docs/permissions.md 에 적어 뒀다.
/// </summary>
[ApiController]
[Route("api/mes/lot")]
[Authorize(Policy = "ViewField")]
public class MesLotController : ControllerBase
{
    private readonly ILotHistoryService _history;
    public MesLotController(ILotHistoryService history) => _history = history;

    /// <summary>
    /// LOT번호 · S/N · 반출번호 중 아무거나 한 칸에 넣고 찾는다(MES 화면의 통합 검색과 같은 규칙).
    /// 찾지 못하면 null 을 돌려준다 — 404 가 아니다. "없음"은 오류가 아니라 정상적인 조회 결과다.
    /// </summary>
    /// <remarks>
    /// 화면이 한 번에 네 덩어리(헤더·TRAN 이력·입출고 사이클·검사 파라미터)를 모두 그리므로
    /// 왕복을 네 번 하지 않고 한 번에 내려준다.
    /// </remarks>
    [HttpGet("history")]
    public async Task<ActionResult<MesLotHistoryDto?>> History([FromQuery] string? keyword, CancellationToken ct)
    {
        var lotId = await _history.FindLotIdByKeywordAsync(keyword, ct);
        if (lotId is null) return Ok(null);

        var header = await _history.GetHeaderAsync(lotId.Value, ct);
        // 아래 셋은 LOT 하나가 아니라 같은 S/N(=같은 물리 부품)의 이력 전체를 모은다.
        // 같은 부품이 여러 번 입고·출고된 내력을 한 화면에서 보려는 것이다.
        var transitions = await _history.GetTransitionsBySerialNumberAsync(header.SerialNumber, ct);
        var cycles = await _history.GetCyclesBySerialNumberAsync(header.SerialNumber, ct);
        var parameters = await _history.GetParameterRecordsBySerialNumberAsync(header.SerialNumber, ct);

        return Ok(new MesLotHistoryDto(header, transitions, cycles, parameters));
    }
}

/// <summary>"LOT 현황 조회" 한 화면이 필요로 하는 전부. 포털 API 전용 묶음이라 API 프로젝트에 둔다.</summary>
public record MesLotHistoryDto(
    LotHistoryHeaderDto Header,
    IReadOnlyList<LotTransitionRowDto> Transitions,
    IReadOnlyList<LotCycleRowDto> Cycles,
    IReadOnlyList<LotParameterRecordDto> Parameters);
