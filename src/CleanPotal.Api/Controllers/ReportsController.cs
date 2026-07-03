using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>회의록/보고서 API — 생산미팅(meeting) · 주간보고(weekly).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _svc;
    public ReportsController(IReportService svc) => _svc = svc;

    /// <summary>type(meeting|weekly)별 월 그룹 목록.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportGroupDto>>> GetGrouped([FromQuery] string type = "meeting")
        => Ok(await _svc.GetGroupedAsync(type));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReportDto>> Get(int id)
    {
        var dto = await _svc.GetAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<ReportDto>> Create([FromBody] ReportUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReportDto>> Update(int id, [FromBody] ReportUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
