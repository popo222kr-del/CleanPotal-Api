using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>스케줄보드(생산 라인 간트) API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ScheduleBoardController : ControllerBase
{
    private readonly IScheduleBoardService _svc;
    public ScheduleBoardController(IScheduleBoardService svc) => _svc = svc;

    [HttpGet("equipments")]
    public ActionResult<IReadOnlyList<ScheduleEquipmentDto>> Equipments() => Ok(_svc.GetEquipments());

    [HttpGet("day")]
    public async Task<ActionResult<IReadOnlyList<ScheduleBlockDto>>> GetDay([FromQuery] string date)
        => Ok(await _svc.GetDayAsync(date));

    [HttpPut("day")]
    public async Task<ActionResult<IReadOnlyList<ScheduleBlockDto>>> SaveDay([FromQuery] string date, [FromBody] ScheduleDaySaveRequest req)
        => Ok(await _svc.SaveDayAsync(date, req.Blocks));

    [HttpGet("recipes")]
    public async Task<ActionResult<IReadOnlyList<ScheduleRecipeDto>>> Recipes()
        => Ok(await _svc.GetRecipesAsync());

    [HttpPost("recipes")]
    public async Task<ActionResult<ScheduleRecipeDto>> AddRecipe([FromBody] ScheduleRecipeAddRequest req)
    {
        var (ok, message, recipe) = await _svc.AddRecipeAsync(req.Text);
        return ok ? Ok(recipe) : BadRequest(new { message });
    }

    [HttpDelete("recipes/{id:int}")]
    public async Task<IActionResult> DeleteRecipe(int id)
        => await _svc.DeleteRecipeAsync(id) ? NoContent() : NotFound();

    [HttpPatch("recipes/{id:int}/favorite")]
    public async Task<ActionResult<ScheduleRecipeDto>> SetFavorite(int id, [FromBody] ScheduleRecipeFavoriteRequest req)
    {
        var dto = await _svc.SetFavoriteAsync(id, req.Favorite);
        return dto is null ? NotFound() : Ok(dto);
    }
}
