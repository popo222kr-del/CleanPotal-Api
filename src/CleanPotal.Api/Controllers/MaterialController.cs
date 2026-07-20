using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>자재물류 일정 현황 API (천안사업장 자재 &amp; 물류 일정).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaterialController : ControllerBase
{
    private readonly IMaterialService _svc;
    public MaterialController(IMaterialService svc) => _svc = svc;

    /// <summary>지정 날짜(기본 오늘)의 표 + 특이사항 + 차량/로스터.</summary>
    [HttpGet]
    public async Task<ActionResult<MaterialDayDto>> GetDay([FromQuery] DateOnly? date)
        => Ok(await _svc.GetDayAsync(date ?? DateOnly.FromDateTime(DateTime.Today)));

    /// <summary>지정 날짜의 표/특이사항 일괄 저장.</summary>
    [Authorize(Policy = "EditSchedule")]
    [HttpPut]
    public async Task<ActionResult<MaterialDayDto>> SaveDay([FromQuery] DateOnly? date, [FromBody] MaterialSaveRequest req)
        => Ok(await _svc.SaveDayAsync(date ?? DateOnly.FromDateTime(DateTime.Today), req));

    /// <summary>담당자 로스터(순서대로).</summary>
    [HttpGet("roster")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoster()
        => Ok(await _svc.GetRosterAsync());

    /// <summary>담당자 로스터 일괄 저장(추가·삭제·순서변경·이름수정). 일정 편집 권한 필요.</summary>
    [HttpPut("roster")]
    [Authorize(Policy = "EditSchedule")]
    public async Task<ActionResult<IReadOnlyList<string>>> SaveRoster([FromBody] MaterialRosterSaveRequest req)
        => Ok(await _svc.SaveRosterAsync(req));

    /// <summary>배차표에서 불러오기 후보(업체+과거 목적지).</summary>
    [HttpGet("destinations")]
    public async Task<ActionResult<IReadOnlyList<MaterialDestinationDto>>> GetDestinations()
        => Ok(await _svc.GetDestinationsAsync());
}
