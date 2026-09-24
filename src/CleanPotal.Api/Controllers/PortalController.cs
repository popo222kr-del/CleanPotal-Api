using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using CleanPotal.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace CleanPotal.Api.Controllers;

/// <summary>업무 파일 통합 관리(포탈) API. 조회는 전체, 관리는 파일 권한(CanManageFiles) 필요.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class PortalController : ControllerBase
{
    private readonly IPortalService _svc;
    private readonly IPortalFileService _files;
    private readonly PortalLaunchTicketStore _launchTickets;
    public PortalController(
        IPortalService svc,
        IPortalFileService files,
        PortalLaunchTicketStore launchTickets)
    {
        _svc = svc;
        _files = files;
        _launchTickets = launchTickets;
    }

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

    /// <summary>
    /// 등록된 파일 바로가기를 다운로드한다. 실제 UNC 경로는 요청으로 받지 않고 DB 항목 ID로만 찾는다.
    /// </summary>
    [HttpGet("items/{id:int}/content")]
    public async Task<IActionResult> DownloadItem(int id, CancellationToken cancellationToken)
    {
        var file = await _files.ResolveAsync(id, cancellationToken);
        if (file is null)
            return NotFound(new { error = "파일이 없거나 허용된 공유폴더 밖의 경로입니다." });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(file.FileName, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(file.FullPath, contentType, file.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// 로컬 실행 도우미용 1회성 실행권을 만든다. 로그인된 사용자가 실제로 열 수 있는 파일에만 발급한다.
    /// </summary>
    [HttpPost("items/{id:int}/launch-ticket")]
    public async Task<IActionResult> CreateLaunchTicket(int id, CancellationToken cancellationToken)
    {
        if (await _files.ResolveAsync(id, cancellationToken) is null)
            return NotFound(new { error = "파일이 없거나 허용된 공유폴더 밖의 경로입니다." });

        var ticket = _launchTickets.Issue(id);
        return Ok(new
        {
            launchUri = $"cleanpotal://open?ticket={ticket.Token}",
            expiresAt = ticket.ExpiresAt
        });
    }

    /// <summary>
    /// 실행 도우미가 1회성 실행권을 파일 경로로 교환한다. 실행권은 20초 후 만료되고 한 번만 쓸 수 있다.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("launch-tickets/{token}/redeem")]
    public async Task<IActionResult> RedeemLaunchTicket(string token, CancellationToken cancellationToken)
    {
        if (!_launchTickets.TryRedeem(token, out var itemId))
            return StatusCode(StatusCodes.Status410Gone, new { error = "실행권이 만료되었거나 이미 사용되었습니다." });

        var file = await _files.ResolveAsync(itemId, cancellationToken);
        if (file is null)
            return NotFound(new { error = "파일이 없거나 허용된 공유폴더 밖의 경로입니다." });

        return Ok(new { path = file.FullPath, fileName = file.FileName });
    }
}
