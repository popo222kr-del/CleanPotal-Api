using System.Security.Claims;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>사용자 계정 관리. 관리자(IsAdmin, DB 기준)만 접근.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "IsAdmin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    private string By => User.FindFirst(ClaimTypes.Name)?.Value ?? "?";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll([FromQuery] bool includeResigned = false)
        => Ok(await _users.GetAllAsync(includeResigned));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> Get(int id)
    {
        var u = await _users.GetAsync(id);
        return u is null ? NotFound() : Ok(u);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] UserUpsertRequest req)
    {
        try
        {
            var dto = await _users.CreateAsync(req, By);
            return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, [FromBody] UserUpsertRequest req)
    {
        try
        {
            var dto = await _users.UpdateAsync(id, req, By);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _users.DeleteAsync(id, By) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>권한 매트릭스 일괄 변경.</summary>
    [HttpPost("perms")]
    public async Task<ActionResult<object>> BulkPerm([FromBody] UserPermBulkRequest req)
        => Ok(new { applied = await _users.BulkPermAsync(req.Changes, By) });

    /// <summary>팀 단위 일괄 변경 (팀명 변경 · 부서 지정).</summary>
    [HttpPost("team-bulk")]
    public async Task<ActionResult<object>> TeamBulk([FromBody] TeamBulkRequest req)
        => Ok(new { count = await _users.TeamBulkAsync(req, By) });

    /// <summary>사용자/권한 변경 감사 로그 (최근 500건).</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<UserAuditDto>>> Audit()
        => Ok(await _users.GetAuditAsync());
}
