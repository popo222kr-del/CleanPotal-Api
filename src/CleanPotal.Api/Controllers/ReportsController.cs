using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 회의록/보고서 API — 생산미팅(meeting, 현장 인수인계 영역) · 주간보고(weekly, OFFICE 영역).
/// 한 API 를 같이 쓰지만 권한은 종류별로 다르다: 클래스 정책("reports" = 둘 중 하나)은 입구만 막고,
/// 각 요청에서 그 보고서 종류의 영역 등급을 다시 본다. 예전에는 인수인계 편집자가 주간보고를 읽고 고칠 수 있었다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewReports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _svc;
    private readonly IAuthorizationService _auth;
    public ReportsController(IReportService svc, IAuthorizationService auth) { _svc = svc; _auth = auth; }

    /// <summary>종류별 정책 이름 — 주간보고=OFFICE, 회의록=현장 인수인계.</summary>
    public static string PolicyFor(string? type, bool edit)
    {
        var area = type == "weekly" ? "Office" : "Handover";
        return (edit ? "Edit" : "View") + area;
    }

    private async Task<bool> Allowed(string? type, bool edit)
        => (await _auth.AuthorizeAsync(User, PolicyFor(type, edit))).Succeeded;

    /// <summary>type(meeting|weekly)별 월 그룹 목록.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportGroupDto>>> GetGrouped([FromQuery] string type = "meeting")
        => await Allowed(type, false) ? Ok(await _svc.GetGroupedAsync(type)) : Forbid();

    /// <summary>전역 검색. type=weekly(기본) → 블록(분류/내용/팔로업) 검색, type=meeting → 주간/야간/Office 메모 검색.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string type = "weekly", [FromQuery] string q = "")
    {
        if (!await Allowed(type, false)) return Forbid();
        return Ok(type == "meeting" ? await _svc.SearchMeetingAsync(q) : await _svc.SearchBlocksAsync(type, q));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReportDto>> Get(int id)
    {
        var dto = await _svc.GetAsync(id);
        if (dto is null) return NotFound();
        return await Allowed(dto.ReportType, false) ? Ok(dto) : Forbid();
    }

    [Authorize(Policy = "EditReports")]
    [HttpPost]
    public async Task<ActionResult<ReportDto>> Create([FromBody] ReportUpsertRequest req)
        => await Allowed(req.ReportType, true) ? Ok(await _svc.CreateAsync(req)) : Forbid();

    [Authorize(Policy = "EditReports")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReportDto>> Update(int id, [FromBody] ReportUpsertRequest req)
    {
        var type = await _svc.GetTypeAsync(id);
        if (type is null) return NotFound();
        if (!await Allowed(type, true)) return Forbid();
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditReports")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var type = await _svc.GetTypeAsync(id);
        if (type is null) return NotFound();
        if (!await Allowed(type, true)) return Forbid();
        return await _svc.DeleteAsync(id) ? NoContent() : NotFound();
    }
}
