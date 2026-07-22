using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>회의록/보고서 API — 생산미팅(meeting) · 주간보고(weekly).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewReports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _svc;
    public ReportsController(IReportService svc) => _svc = svc;

    /// <summary>type(meeting|weekly)별 월 그룹 목록.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportGroupDto>>> GetGrouped([FromQuery] string type = "meeting")
        => Ok(await _svc.GetGroupedAsync(type));

    /// <summary>전역 블록 검색 (주간보고 전체 검색).</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<ReportSearchHitDto>>> Search([FromQuery] string type = "weekly", [FromQuery] string q = "")
        => Ok(await _svc.SearchBlocksAsync(type, q));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReportDto>> Get(int id)
    {
        var dto = await _svc.GetAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditReports")]
    [HttpPost]
    public async Task<ActionResult<ReportDto>> Create([FromBody] ReportUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [Authorize(Policy = "EditReports")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReportDto>> Update(int id, [FromBody] ReportUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditReports")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
