using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>개인별 업무 분장표 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class WorkAssignmentController : ControllerBase
{
    private readonly IWorkAssignmentService _svc;
    public WorkAssignmentController(IWorkAssignmentService svc) => _svc = svc;

    // ── 인원 ──
    [HttpGet("members")]
    public async Task<ActionResult<IReadOnlyList<WorkMemberDto>>> GetMembers([FromQuery] bool includeHidden = false)
        => Ok(await _svc.GetMembersAsync(includeHidden));

    [HttpGet("members/{username}")]
    public async Task<ActionResult<WorkMemberDetailDto>> GetMember(string username)
    {
        var dto = await _svc.GetMemberAsync(username);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpPost("members")]
    public async Task<ActionResult<WorkMemberDto>> AddMember([FromBody] WorkMemberUpsertRequest req)
        => Ok(await _svc.AddMemberAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("members/{id:int}")]
    public async Task<ActionResult<WorkMemberDto>> UpdateMember(int id, [FromBody] WorkMemberUpsertRequest req)
    {
        var dto = await _svc.UpdateMemberAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("members/{id:int}")]
    public async Task<IActionResult> DeleteMember(int id)
        => await _svc.DeleteMemberAsync(id) ? NoContent() : NotFound();

    // ── 계정 ──
    [Authorize(Policy = "EditOffice")]
    [HttpPost("accounts")]
    public async Task<ActionResult<WorkAccountDto>> AddAccount([FromBody] WorkAccountUpsertRequest req)
        => Ok(await _svc.SaveAccountAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("accounts/{id:int}")]
    public async Task<ActionResult<WorkAccountDto>> UpdateAccount(int id, [FromBody] WorkAccountUpsertRequest req)
    {
        var dto = await _svc.UpdateAccountAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("accounts/{id:int}")]
    public async Task<IActionResult> DeleteAccount(int id)
        => await _svc.DeleteAccountAsync(id) ? NoContent() : NotFound();

    // ── 교육 이수 ──
    [Authorize(Policy = "EditOffice")]
    [HttpPost("edus")]
    public async Task<ActionResult<WorkEduDto>> AddEdu([FromBody] WorkEduUpsertRequest req)
        => Ok(await _svc.SaveEduAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("edus/{id:int}")]
    public async Task<ActionResult<WorkEduDto>> UpdateEdu(int id, [FromBody] WorkEduUpsertRequest req)
    {
        var dto = await _svc.UpdateEduAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("edus/{id:int}")]
    public async Task<IActionResult> DeleteEdu(int id)
        => await _svc.DeleteEduAsync(id) ? NoContent() : NotFound();
}
