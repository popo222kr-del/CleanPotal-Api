using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>BROKEN(파손/불량) 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
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

    [Authorize(Policy = "EditOffice")]
    [HttpPost]
    public async Task<ActionResult<BrokenRecordDto>> Create([FromBody] BrokenUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<BrokenRecordDto>> Update(int id, [FromBody] BrokenUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();

    /// <summary>유발자 자동 완성용 사용자 디렉터리 (이름/직위/입사일).</summary>
    [HttpGet("user-directory")]
    public async Task<ActionResult<IReadOnlyList<BrokenUserDto>>> UserDirectory()
        => Ok(await _svc.GetUserDirectoryAsync());

    // ── 교육 기록 ──
    [HttpGet("trainings")]
    public async Task<ActionResult<IReadOnlyList<BrokenTrainingDto>>> GetTrainings([FromQuery] string? type)
        => Ok(await _svc.GetTrainingsAsync(type));

    [Authorize(Policy = "EditOffice")]
    [HttpPost("trainings")]
    public async Task<ActionResult<BrokenTrainingDto>> CreateTraining([FromBody] BrokenTrainingUpsertRequest req)
        => Ok(await _svc.CreateTrainingAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("trainings/{id:int}")]
    public async Task<ActionResult<BrokenTrainingDto>> UpdateTraining(int id, [FromBody] BrokenTrainingUpsertRequest req)
    {
        var dto = await _svc.UpdateTrainingAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("trainings/{id:int}")]
    public async Task<IActionResult> DeleteTraining(int id)
        => await _svc.DeleteTrainingAsync(id) ? NoContent() : NotFound();

    // ── 교육 목표 / 메모 ──
    [HttpGet("goals")]
    public async Task<ActionResult<IReadOnlyList<BrokenGoalDto>>> GetGoals()
        => Ok(await _svc.GetGoalsAsync());

    [Authorize(Policy = "EditOffice")]
    [HttpPut("goals")]
    public async Task<ActionResult<IReadOnlyList<BrokenGoalDto>>> SaveGoals([FromBody] IReadOnlyList<BrokenGoalInput> goals)
        => Ok(await _svc.SaveGoalsAsync(goals));

    [HttpGet("memo")]
    public async Task<ActionResult<BrokenMemoDto>> GetMemo()
        => Ok(await _svc.GetMemoAsync());

    [Authorize(Policy = "EditOffice")]
    [HttpPut("memo")]
    public async Task<ActionResult<BrokenMemoDto>> SaveMemo([FromBody] BrokenMemoDto req)
        => Ok(await _svc.SaveMemoAsync(req));
}
