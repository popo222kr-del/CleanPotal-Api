using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>생산팀 요청사항 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class ProdReqController : ControllerBase
{
    private readonly IProdReqService _svc;
    public ProdReqController(IProdReqService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";
    // 읽음 상태 키: 사번(sub) 우선, 없으면 실명
    private string ReadKey =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? Actor;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProdReqDto>>> GetAll([FromQuery] string? status, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(status, search));

    /// <summary>미확인(새 요청) 개수 — 사이드바 뱃지용.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<ProdReqUnreadDto>> UnreadCount()
        => Ok(new ProdReqUnreadDto(await _svc.GetUnreadCountAsync(ReadKey)));

    /// <summary>페이지 진입 시 읽음 처리 — 뱃지 초기화.</summary>
    [Authorize(Policy = "EditHandover")]
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead()
    {
        await _svc.MarkReadAsync(ReadKey);
        return NoContent();
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPost]
    public async Task<ActionResult<ProdReqDto>> Create([FromBody] ProdReqUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProdReqDto>> Update(int id, [FromBody] ProdReqUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req, Actor);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ProdReqDto>> ChangeStatus(int id, [FromBody] ProdReqStatusRequest req)
    {
        var dto = await _svc.ChangeStatusAsync(id, req.Status);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
