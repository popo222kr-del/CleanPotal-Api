using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>사무실 공지 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NoticeController : ControllerBase
{
    private readonly INoticeService _svc;
    public NoticeController(INoticeService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoticeDto>>> GetAll()
        => Ok(await _svc.GetAllAsync());

    [HttpPost]
    public async Task<ActionResult<NoticeDto>> Create([FromBody] NoticeUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<NoticeDto>> Update(int id, [FromBody] NoticeUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
