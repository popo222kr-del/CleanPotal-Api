using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>업체 관리 API. 관리는 업체 권한(CanManageVendors).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class VendorController : ControllerBase
{
    private readonly IVendorService _svc;
    public VendorController(IVendorService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VendorDto>>> GetAll([FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(search));

    [HttpPost]
    [Authorize(Policy = "EditHandover")]
    public async Task<ActionResult<VendorDto>> Create([FromBody] VendorUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    [Authorize(Policy = "EditHandover")]
    public async Task<ActionResult<VendorDto>> Update(int id, [FromBody] VendorUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "EditHandover")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();

    /// <summary>즐겨찾기 토글 — 다른 필드는 건드리지 않음 (동시 수정 덮어쓰기 방지).</summary>
    [HttpPost("{id:int}/favorite")]
    [Authorize(Policy = "EditHandover")]
    public async Task<ActionResult<object>> ToggleFavorite(int id)
    {
        var fav = await _svc.ToggleFavoriteAsync(id);
        return fav is null ? NotFound() : Ok(new { isFavorite = fav });
    }
}
