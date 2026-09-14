using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 세정 레시피 관리 API. 스케줄보드(세정 레시피)와 같은 도메인이므로
/// 조회는 ViewHandover, 변경은 EditHandover 정책을 따른다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecipeController : ControllerBase
{
    private readonly IRecipeService _svc;
    public RecipeController(IRecipeService svc) => _svc = svc;

    [Authorize(Policy = "ViewHandover")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeDto>>> GetAll([FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(search));

    [Authorize(Policy = "EditHandover")]
    [HttpPost]
    public async Task<ActionResult<RecipeDto>> Create([FromBody] RecipeUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<RecipeDto>> Update(int id, [FromBody] RecipeUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
