using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>업체 견적서 API.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class QuotationController : ControllerBase
{
    private readonly IQuotationService _svc;
    public QuotationController(IQuotationService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuotationSummaryDto>>> GetAll([FromQuery] string? vendor, [FromQuery] string? search)
        => Ok(await _svc.GetAllAsync(vendor, search));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuotationDto>> Get(int id)
    {
        var dto = await _svc.GetAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpPost]
    public async Task<ActionResult<QuotationDto>> Create([FromBody] QuotationUpsertRequest req)
        => Ok(await _svc.CreateAsync(req, Actor));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<QuotationDto>> Update(int id, [FromBody] QuotationUpsertRequest req)
    {
        var dto = await _svc.UpdateAsync(id, req, Actor);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _svc.DeleteAsync(id) ? NoContent() : NotFound();
}
