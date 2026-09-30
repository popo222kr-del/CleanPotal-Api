using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 설비 목록(읽기 전용) — 스케줄 보드 설비 표가 모든 화면의 설비 목록이다.
/// MES 설비호기 고르기·설비 체크시트 등 여러 영역 화면이 같이 쓴다(설비 이름·라인·종류·공정만).
/// 고치는 곳은 스케줄 보드 '설비 &amp; 레시피 관리'(/api/scheduleboard/equipments).
/// </summary>
[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly IScheduleBoardService _board;
    public EquipmentController(IScheduleBoardService board) => _board = board;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScheduleEquipmentDto>>> List() => Ok(await _board.GetEquipmentsAsync(includeHidden: true));
}
