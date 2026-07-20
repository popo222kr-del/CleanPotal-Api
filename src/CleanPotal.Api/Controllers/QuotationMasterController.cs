using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>견적 마스터 데이터 — 품목 단가표 · 전역 템플릿 · 견적 설정.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ViewOffice")]
public class QuotationMasterController : ControllerBase
{
    private readonly IQuotationMasterService _svc;
    public QuotationMasterController(IQuotationMasterService svc) => _svc = svc;

    private string Actor => User.Identity?.Name ?? "system";

    // ── 품목 단가표 ──
    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<ProductMasterDto>>> GetProducts([FromQuery] string? search)
        => Ok(await _svc.GetProductsAsync(search));

    [Authorize(Policy = "EditOffice")]
    [HttpPost("products")]
    public async Task<ActionResult<ProductMasterDto>> CreateProduct([FromBody] ProductMasterUpsertRequest req)
        => Ok(await _svc.CreateProductAsync(req, Actor));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("products/{id:int}")]
    public async Task<ActionResult<ProductMasterDto>> UpdateProduct(int id, [FromBody] ProductMasterUpsertRequest req)
    {
        var dto = await _svc.UpdateProductAsync(id, req, Actor);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("products/{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
        => await _svc.DeleteProductAsync(id) ? NoContent() : NotFound();

    // ── 전역 품목 템플릿 ──
    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<GlobalTemplateDto>>> GetTemplates()
        => Ok(await _svc.GetTemplatesAsync());

    [Authorize(Policy = "EditOffice")]
    [HttpPost("templates")]
    public async Task<ActionResult<GlobalTemplateDto>> CreateTemplate([FromBody] GlobalTemplateUpsertRequest req)
        => Ok(await _svc.CreateTemplateAsync(req));

    [Authorize(Policy = "EditOffice")]
    [HttpPut("templates/{id:int}")]
    public async Task<ActionResult<GlobalTemplateDto>> UpdateTemplate(int id, [FromBody] GlobalTemplateUpsertRequest req)
    {
        var dto = await _svc.UpdateTemplateAsync(id, req);
        return dto is null ? NotFound() : Ok(dto);
    }

    [Authorize(Policy = "EditOffice")]
    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
        => await _svc.DeleteTemplateAsync(id) ? NoContent() : NotFound();

    // ── 견적 설정 ──
    [HttpGet("config")]
    public async Task<ActionResult<QuotationConfigDto>> GetConfig()
        => Ok(await _svc.GetConfigAsync());

    [Authorize(Policy = "EditOffice")]
    [HttpPut("config")]
    public async Task<ActionResult<QuotationConfigDto>> SaveConfig([FromBody] QuotationConfigDto req)
        => Ok(await _svc.SaveConfigAsync(req));
}
