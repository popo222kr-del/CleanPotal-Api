using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>업무 파일 통합 관리(포탈) API. 조회는 전체, 관리는 파일 권한(CanManageFiles) 필요.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class PortalController : ControllerBase
{
    private readonly IPortalService _svc;
    public PortalController(IPortalService svc) => _svc = svc;

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<PortalGroupDto>>> GetAll()
        => Ok(await _svc.GetAllAsync());

    // ── 그룹 관리 (파일 권한) ──
    [HttpPost("groups")]
    [Authorize(Policy = "EditOffice")]
    public async Task<ActionResult<PortalGroupDto>> AddGroup([FromBody] PortalGroupRequest req)
        => Ok(await _svc.AddGroupAsync(req));

    [HttpPut("groups/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    public async Task<ActionResult<PortalGroupDto>> RenameGroup(int id, [FromBody] PortalGroupRequest req)
    {
        var dto = await _svc.RenameGroupAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("groups/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    public async Task<IActionResult> DeleteGroup(int id)
        => await _svc.DeleteGroupAsync(id) ? NoContent() : NotFound();

    // ── 항목 관리 (파일 권한) ──
    [HttpPost("items")]
    [Authorize(Policy = "EditOffice")]
    public async Task<ActionResult<PortalItemDto>> AddItem([FromBody] PortalItemRequest req)
        => Ok(await _svc.AddItemAsync(req));

    [HttpPut("items/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    public async Task<ActionResult<PortalItemDto>> UpdateItem(int id, [FromBody] PortalItemRequest req)
    {
        var dto = await _svc.UpdateItemAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("items/{id:int}")]
    [Authorize(Policy = "EditOffice")]
    public async Task<IActionResult> DeleteItem(int id)
        => await _svc.DeleteItemAsync(id) ? NoContent() : NotFound();
}
