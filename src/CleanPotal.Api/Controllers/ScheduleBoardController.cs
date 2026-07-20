using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>스케줄보드(생산 라인 간트) API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewHandover")]
public class ScheduleBoardController : ControllerBase
{
    private readonly IScheduleBoardService _svc;
    public ScheduleBoardController(IScheduleBoardService svc) => _svc = svc;

    [HttpGet("equipments")]
    public async Task<ActionResult<IReadOnlyList<ScheduleEquipmentDto>>> Equipments()
        => Ok(await _svc.GetEquipmentsAsync());

    [Authorize(Policy = "EditHandover")]
    [HttpPost("equipments")]
    public async Task<ActionResult<ScheduleEquipmentDto>> AddEquipment([FromBody] ScheduleEquipmentUpsertRequest req)
        => Ok(await _svc.AddEquipmentAsync(req.Name, req.GroupName, req.Process, req.Note, req.IsIdle));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("equipments/{id:int}")]
    public async Task<ActionResult<ScheduleEquipmentDto>> UpdateEquipment(int id, [FromBody] ScheduleEquipmentUpsertRequest req)
    {
        var dto = await _svc.UpdateEquipmentAsync(id, req.Name, req.GroupName, req.Process, req.Note, req.IsIdle);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("equipments/{id:int}")]
    public async Task<IActionResult> DeleteEquipment(int id)
        => await _svc.DeleteEquipmentAsync(id) ? NoContent() : NotFound();

    [Authorize(Policy = "EditHandover")]
    [HttpPost("equipments/reorder")]
    public async Task<IActionResult> ReorderEquipments([FromBody] ScheduleReorderRequest req)
    {
        await _svc.ReorderEquipmentsAsync(req.Ids);
        return NoContent();
    }

    [HttpGet("day")]
    public async Task<ActionResult<IReadOnlyList<ScheduleBlockDto>>> GetDay([FromQuery] string date)
        => Ok(await _svc.GetDayAsync(date));

    [Authorize(Policy = "EditHandover")]
    [HttpPut("day")]
    public async Task<ActionResult<IReadOnlyList<ScheduleBlockDto>>> SaveDay([FromQuery] string date, [FromBody] ScheduleDaySaveRequest req)
        => Ok(await _svc.SaveDayAsync(date, req.Blocks));

    [HttpGet("recipes")]
    public async Task<ActionResult<IReadOnlyList<ScheduleRecipeDto>>> Recipes()
        => Ok(await _svc.GetRecipesAsync());

    [Authorize(Policy = "EditHandover")]
    [HttpPost("recipes")]
    public async Task<ActionResult<ScheduleRecipeDto>> AddRecipe([FromBody] ScheduleRecipeAddRequest req)
    {
        var (ok, message, recipe) = await _svc.AddRecipeAsync(req.Text);
        // EnvelopeResultFilter는 'error' 속성만 읽으므로 error로 반환해야 실제 사유가 전달됨
        return ok ? Ok(recipe) : BadRequest(new { error = message });
    }

    [Authorize(Policy = "EditHandover")]
    [HttpPut("recipes/{id:int}")]
    public async Task<ActionResult<ScheduleRecipeDto>> UpdateRecipe(int id, [FromBody] ScheduleRecipeUpdateRequest req)
    {
        var (ok, message, recipe) = await _svc.UpdateRecipeAsync(id, req.S2Minutes, req.HFMinutes, req.DIMinutes, req.S2Temperature);
        return ok ? Ok(recipe) : BadRequest(new { error = message });
    }

    [Authorize(Policy = "EditHandover")]
    [HttpDelete("recipes/{id:int}")]
    public async Task<IActionResult> DeleteRecipe(int id)
        => await _svc.DeleteRecipeAsync(id) ? NoContent() : NotFound();

    [Authorize(Policy = "EditHandover")]
    [HttpPatch("recipes/{id:int}/favorite")]
    public async Task<ActionResult<ScheduleRecipeDto>> SetFavorite(int id, [FromBody] ScheduleRecipeFavoriteRequest req)
    {
        var dto = await _svc.SetFavoriteAsync(id, req.Favorite);
        return dto is null ? NotFound() : Ok(dto);
    }
}
