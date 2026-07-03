using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>BROKEN(파손/불량) 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrokenController : ControllerBase
{
    private readonly IBrokenService _svc;
    public BrokenController(IBrokenService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrokenRecordDto>>> GetAll(
        [FromQuery] int? year, [FromQuery] string? team, [FromQuery] string? productType,
        [FromQuery] string? official, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(year, team, productType, official, search));

    [HttpGet("filters")]
    public async Task<ActionResult<BrokenFilterOptionsDto>> Filters()
        => Ok(await _svc.GetFilterOptionsAsync());

    [HttpPost]
    public async Task<ActionResult<BrokenRecordDto>> Create([FromBody] BrokenUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BrokenRecordDto>> Update(int id, [FromBody] BrokenUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();

    // ── 교육 기록 ──
    [HttpGet("trainings")]
    public async Task<ActionResult<IReadOnlyList<BrokenTrainingDto>>> GetTrainings([FromQuery] string? type)
        => Ok(await _svc.GetTrainingsAsync(type));

    [HttpPost("trainings")]
    public async Task<ActionResult<BrokenTrainingDto>> CreateTraining([FromBody] BrokenTrainingUpsertRequest req)
        => Ok(await _svc.CreateTrainingAsync(req));

    [HttpPut("trainings/{id:int}")]
    public async Task<ActionResult<BrokenTrainingDto>> UpdateTraining(int id, [FromBody] BrokenTrainingUpsertRequest req)
    {
        var dto = await _svc.UpdateTrainingAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("trainings/{id:int}")]
    public async Task<IActionResult> DeleteTraining(int id)
        => await _svc.DeleteTrainingAsync(id) ? NoContent() : NotFound();

    // ── 교육 목표 / 메모 ──
    [HttpGet("goals")]
    public async Task<ActionResult<IReadOnlyList<BrokenGoalDto>>> GetGoals()
        => Ok(await _svc.GetGoalsAsync());

    [HttpPut("goals")]
    public async Task<ActionResult<IReadOnlyList<BrokenGoalDto>>> SaveGoals([FromBody] IReadOnlyList<BrokenGoalInput> goals)
        => Ok(await _svc.SaveGoalsAsync(goals));

    [HttpGet("memo")]
    public async Task<ActionResult<BrokenMemoDto>> GetMemo()
        => Ok(await _svc.GetMemoAsync());

    [HttpPut("memo")]
    public async Task<ActionResult<BrokenMemoDto>> SaveMemo([FromBody] BrokenMemoDto req)
        => Ok(await _svc.SaveMemoAsync(req));
}
