using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionManagement.Infrastructure.Data;

namespace CleanPotal.Api.Controllers;

/// <summary>BROKEN(파손/불량) 관리 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
[CleanPotal.Api.Infrastructure.MenuGate("/broken")]
public class BrokenController : ControllerBase
{
    private readonly IBrokenService _svc;
    private readonly ApplicationDbContext _mesDb;
    public BrokenController(IBrokenService svc, ApplicationDbContext mesDb)
    {
        _svc = svc;
        _mesDb = mesDb;
    }

    // 라인 목록은 MES 가 주인이다. MES DB 에 닿지 못해도 BROKEN 화면까지 멈추면 안 되므로
    // 실패하면 빈 목록을 주고, 서비스가 기록에 남은 라인으로 채운다.
    private async Task<IReadOnlyList<string>> MesLinesAsync()
    {
        try
        {
            return await _mesDb.LineDefinitions
                .Where(l => l.IsActive)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .Select(l => l.Code)
                .ToListAsync();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrokenRecordDto>>> GetAll(
        [FromQuery] int? year, [FromQuery] string? team, [FromQuery] string? productType,
        [FromQuery] string? official, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(year, team, productType, official, search));

    [HttpGet("filters")]
    public async Task<ActionResult<BrokenFilterOptionsDto>> Filters()
        => Ok(await _svc.GetFilterOptionsAsync());

    /// <summary>등록 칸 드롭다운 목록 (제품군·발생단계·팀·라인).</summary>
    [HttpGet("options")]
    public async Task<ActionResult<BrokenOptionsDto>> GetOptions()
        => Ok(await _svc.GetOptionsAsync(await MesLinesAsync()));

    /// <summary>제품군·발생단계 목록 전체 저장. 팀·라인은 각자의 마스터에서 오므로 여기서 못 고친다.</summary>
    [Authorize(Policy = "EditOffice")]
    [HttpPut("options")]
    public async Task<ActionResult<BrokenOptionsDto>> SaveOptions([FromBody] BrokenOptionsSaveRequest req)
        => Ok(await _svc.SaveOptionsAsync(req, await MesLinesAsync()));

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
