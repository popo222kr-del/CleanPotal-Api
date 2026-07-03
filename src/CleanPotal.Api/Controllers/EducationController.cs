using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>교육 현황 대시보드 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EducationController : ControllerBase
{
    private readonly IEducationService _svc;
    public EducationController(IEducationService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EducationPlanDto>>> GetAll(
        [FromQuery] int? year, [FromQuery] string? status, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(year, status, search));

    [HttpPost]
    public async Task<ActionResult<EducationPlanDto>> Create([FromBody] EducationUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EducationPlanDto>> Update(int id, [FromBody] EducationUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
