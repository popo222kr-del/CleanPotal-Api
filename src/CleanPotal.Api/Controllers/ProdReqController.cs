using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>생산팀 요청사항 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProdReqController : ControllerBase
{
    private readonly IProdReqService _svc;
    public ProdReqController(IProdReqService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProdReqDto>>> GetAll([FromQuery] string? status, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(status, search));

    [HttpPost]
    public async Task<ActionResult<ProdReqDto>> Create([FromBody] ProdReqUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProdReqDto>> Update(int id, [FromBody] ProdReqUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ProdReqDto>> ChangeStatus(int id, [FromBody] ProdReqStatusRequest req)
    {
        var dto = await _svc.ChangeStatusAsync(id, req.Status);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
