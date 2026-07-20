using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>교육 현황 대시보드 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class EducationController : ControllerBase
{
    private readonly IEducationService _svc;
    public EducationController(IEducationService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EducationPlanDto>>> GetAll(
        [FromQuery] int? year, [FromQuery] string? status, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(year, status, search));

    [Authorize(Policy = "EditOffice")]
    [HttpPost]
    public async Task<ActionResult<EducationPlanDto>> Create([FromBody] EducationUpsertRequest req)
        => Ok(await _svc.CreateAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<EducationPlanDto>> Update(int id, [FromBody] EducationUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
