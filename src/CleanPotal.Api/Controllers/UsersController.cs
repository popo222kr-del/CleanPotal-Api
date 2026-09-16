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

    /// <summary>부서명 일괄 변경 (해당 부서 전원).</summary>
    [HttpPost("dept-bulk")]
    public async Task<ActionResult<object>> DeptBulk([FromBody] DeptBulkRequest req)
        => Ok(new { count = await _users.DeptBulkAsync(req.OldDept, req.NewDept, By) });

    /// <summary>조직도(본부→부서→팀→인원) 조회.</summary>
    [HttpGet("org")]
    public async Task<ActionResult<OrgTreeDto>> Org()
        => Ok(await _users.GetOrgAsync());

    /// <summary>부서를 본부에 연결. POST /api/users/org/dept-division</summary>
    [HttpPost("org/dept-division")]
    public async Task<ActionResult<object>> OrgDeptDivision([FromBody] OrgDeptDivisionRequest req)
    {
        var err = await _users.SetDeptDivisionAsync(req.Dept, req.Division, By);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>본부 이름 변경. POST /api/users/org/division-rename</summary>
    [HttpPost("org/division-rename")]
    public async Task<ActionResult<object>> OrgDivisionRename([FromBody] OrgDivisionRenameRequest req)
    {
        var err = await _users.RenameDivisionAsync(req.OldName, req.NewName, By);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>본부/부서/팀 추가.</summary>
    [HttpPost("org/add")]
    public async Task<ActionResult<object>> OrgAdd([FromBody] OrgUnitRequest req)
    {
        var err = await _users.AddOrgAsync(req.Kind, req.Name, req.Parent, By);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>부서/팀 삭제 (소속 인원 없을 때만).</summary>
    /// <summary>팀의 교대 조 지정 (0=없음/1조/2조). POST /api/users/org/shift</summary>
    [HttpPost("org/shift")]
    public async Task<ActionResult<object>> OrgShift([FromBody] OrgShiftGroupRequest req)
    {
        var err = await _users.SetOrgShiftGroupAsync(req.Name, req.ShiftGroup, By, req.Parent);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>팀의 생산팀 여부 지정. POST /api/users/org/production</summary>
    [HttpPost("org/production")]
    public async Task<ActionResult<object>> OrgProduction([FromBody] OrgProductionRequest req)
    {
        var err = await _users.SetOrgProductionAsync(req.Name, req.IsProduction, By, req.Parent);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>팀의 WPF 옛 이름 지정. POST /api/users/org/legacy-names</summary>
    [HttpPost("org/legacy-names")]
    public async Task<ActionResult<object>> OrgLegacyNames([FromBody] OrgLegacyNamesRequest req)
    {
        var err = await _users.SetOrgLegacyNamesAsync(req.Name, req.LegacyNames, By, req.Parent);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>부서 달력 표시 설정(색·약칭). POST /api/users/org/dept-style</summary>
    [HttpPost("org/dept-style")]
    public async Task<ActionResult<object>> OrgDeptStyle([FromBody] OrgDeptStyleRequest req)
    {
        var err = await _users.SetDeptStyleAsync(req.Name, req.Color, req.ShortName, By);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    [HttpPost("org/delete")]
    public async Task<ActionResult<object>> OrgDelete([FromBody] OrgUnitRequest req)
    {
        var err = await _users.DeleteOrgAsync(req.Kind, req.Name, req.Parent, By);
        return err is null ? Ok(new { ok = true }) : BadRequest(new { error = err });
    }

    /// <summary>사용자/권한 변경 감사 로그 (최근 500건).</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<UserAuditDto>>> Audit()
        => Ok(await _users.GetAuditAsync());
}
