using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 셋업 &gt; 제품 셋업 — 제품 마스터와 거기 딸린 것들(공정 플로우 · 공정별 레시피 · 검사 파라미터 ·
/// 성적서 기본 양식 · 기본 LINE).
///
/// 제품 하나에 붙는 설정이 다섯 갈래라 조회는 한 번에 묶어 내려주고, 바꾸는 것은 갈래별로 나눠 둔다
/// — 한 덩어리로 저장하면 레시피 하나 고치는 데 파라미터까지 덮어쓰게 된다.
/// </summary>
[ApiController]
[Route("api/mes/setup/product")]
[Authorize(Policy = "ViewMes")]
public class MesProductSetupController : MesSetupControllerBase
{
    private const long MaxTemplateBytes = 30L * 1024 * 1024;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly IProductFlowService _flows;
    private readonly IProductReferenceDataService _refData;
    private readonly IProductPriceService _prices;

    public MesProductSetupController(
        IProductService products,
        ICustomerService customers,
        IProductFlowService flows,
        IProductReferenceDataService refData,
        IProductPriceService prices,
        ILogger<MesProductSetupController> log) : base(log)
    {
        _products = products;
        _customers = customers;
        _flows = flows;
        _refData = refData;
        _prices = prices;
    }

    /// <summary>화면이 한 번만 받아 두면 되는 고르는 목록들(업체 · LINE · 레시피 카탈로그 · 파라미터 카탈로그 · OPER).</summary>
    [HttpGet("reference")]
    public async Task<ActionResult<MesProductReferenceDto>> Reference(CancellationToken ct)
        => Ok(new MesProductReferenceDto(
            await _customers.GetAllAsync(ct),
            await _refData.GetLinesAsync(ct),
            await _refData.GetRecipesAsync(ct),
            await _refData.GetParameterCatalogAsync(ct),
            await _refData.GetOperOptionsAsync(ct)));

    /// <summary>제품 목록.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> List(CancellationToken ct)
        => Ok(await _products.GetAllAsync(ct));

    /// <summary>고른 제품에 딸린 것 전부. 화면이 한 화면에 같이 그리므로 왕복도 한 번이다.</summary>
    [HttpGet("{productId:int}")]
    public async Task<ActionResult<MesProductDetailDto>> Detail(int productId, CancellationToken ct)
    {
        var (_, templateFileName) = await _prices.GetTemplateAsync(productId, ct);
        return Ok(new MesProductDetailDto(
            await _flows.GetAssignedFlowsAsync(productId, ct),
            await _flows.GetAvailableFlowsAsync(productId, ct),
            await _refData.GetProductDefaultLineIdAsync(productId, ct),
            await _refData.GetProductRecipesAsync(productId, ct),
            await _refData.GetProductParametersAsync(productId, ct),
            templateFileName));
    }

    // ── 제품 ───────────────────────────────────────────────────────────────

    [Authorize(Policy = "EditMes")]
    [HttpPost]
    public Task<ActionResult<MesSetupResultDto>> Create([FromBody] ProductUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _products.CreateAsync(Trim(request), ct), "저장되었습니다.", "제품 등록");

    /// <summary>
    /// 고른 제품을 본떠 새 세정코드를 만든다 — 레시피 · 검사 파라미터 · 기본 LINE 까지 따라온다.
    ///
    /// 비슷한 제품이 계속 들어오는 일이라, 매번 레시피와 검사 항목을 손으로 다시 넣으면 빠뜨린다.
    /// 원본을 주지 않으면 그냥 새 제품을 만드는 것과 같다.
    /// 제품은 만들어졌는데 복사에서 실패하면 그 사실을 알린다 — 조용히 반쪽만 만들어 두지 않는다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("create-from")]
    public async Task<ActionResult<MesSetupResultDto>> CreateFrom(
        [FromBody] MesCreateProductFromRequest request, CancellationToken ct)
    {
        var source = (request.SourceCleaningCode ?? "").Trim();
        var target = (request.Product.CleaningCode ?? "").Trim();
        if (target.Length == 0)
            return Ok(new MesSetupResultDto(false, "세정코드를 입력하세요."));
        if (source.Length > 0 && string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return Ok(new MesSetupResultDto(false, "신규 생성하려면 세정코드를 바꿔야 합니다."));

        var created = await RunAsync(
            async () =>
            {
                var product = await _products.CreateAsync(Trim(request.Product), ct);
                if (source.Length == 0) return;

                await _refData.CopyProductRecipesAsync(product.ProductId, source, ct);
                await _refData.CopyParametersAsync(product.ProductId, source, ct);

                // 기본 LINE 은 원본에 있을 때만 따라온다.
                if (request.SourceProductId is { } sourceId)
                {
                    var line = await _refData.GetProductDefaultLineIdAsync(sourceId, ct);
                    if (line is not null)
                        await _refData.SetProductDefaultLineAsync(product.ProductId, line, ct);
                }
            },
            source.Length == 0 ? "새 세정코드를 만들었습니다." : "원본을 본떠 새 세정코드를 만들었습니다.",
            "제품 생성");
        return created;
    }

    [Authorize(Policy = "EditMes")]
    [HttpPut("{productId:int}")]
    public Task<ActionResult<MesSetupResultDto>> Update(
        int productId, [FromBody] ProductUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _products.UpdateAsync(productId, Trim(request), ct), "저장되었습니다.", "제품 수정");

    /// <summary>제품 중지·활성화. 지우지 않는다 — 과거 LOT 이 이 제품을 가리킨다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/active")]
    public Task<ActionResult<MesSetupResultDto>> SetActive(
        int productId, [FromBody] MesActiveRequest request, CancellationToken ct)
        => RunAsync(() => _products.SetActiveAsync(productId, request.IsActive, ct),
            request.IsActive ? "활성화했습니다." : "중지했습니다.", "제품 상태 변경");

    /// <summary>전산등록에서 기본으로 채워질 LINE.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/default-line")]
    public Task<ActionResult<MesSetupResultDto>> SetDefaultLine(
        int productId, [FromBody] MesDefaultLineRequest request, CancellationToken ct)
        => RunAsync(() => _refData.SetProductDefaultLineAsync(productId, request.LineId, ct),
            "기본 LINE 을 저장했습니다.", "기본 LINE 저장");

    // ── 공정 플로우 ────────────────────────────────────────────────────────

    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/flows/{processRouteId:int}")]
    public Task<ActionResult<MesSetupResultDto>> AssignFlow(int productId, int processRouteId, CancellationToken ct)
        => RunAsync(() => _flows.AssignFlowAsync(productId, processRouteId, ct), "플로우를 부여했습니다.", "플로우 부여");

    [Authorize(Policy = "EditMes")]
    [HttpDelete("{productId:int}/flows/{processRouteId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UnassignFlow(int productId, int processRouteId, CancellationToken ct)
        => RunAsync(() => _flows.UnassignFlowAsync(productId, processRouteId, ct), "플로우를 해제했습니다.", "플로우 해제");

    // ── 성적서 기본 양식 ───────────────────────────────────────────────────

    /// <summary>이 제품의 성적서 기본 양식을 받는다. 없으면 404 로 알린다.</summary>
    [HttpGet("{productId:int}/template")]
    public async Task<IActionResult> Template(int productId, CancellationToken ct)
    {
        var (data, fileName) = await _prices.GetTemplateAsync(productId, ct);
        if (data is not { Length: > 0 })
            return NotFound(new { error = "이 제품의 성적서 기본 양식이 없습니다." });
        return File(data, XlsxContentType, fileName ?? $"template-{productId}.xlsx");
    }

    /// <summary>
    /// 성적서 기본 양식을 올린다. 파일을 안 보내고 이름만 보내면 이름만 바꾼다
    /// (INSPECTION 폴더에 이미 있는 양식을 가리키는 경우) — MES 화면에서 이름을 손으로 칠 수 있던 것과 같다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/template")]
    [RequestSizeLimit(MaxTemplateBytes)]
    public async Task<ActionResult<MesSetupResultDto>> SetTemplate(
        int productId, IFormFile? file, [FromForm] string? fileName, CancellationToken ct)
    {
        byte[]? data = null;
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxTemplateBytes)
                return Ok(new MesSetupResultDto(false, "양식 파일이 너무 큽니다(최대 30MB)."));
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            data = buffer.ToArray();
        }

        var name = string.IsNullOrWhiteSpace(fileName) ? file?.FileName : fileName.Trim();
        return await RunAsync(() => _prices.SetTemplateAsync(productId, data, name, ct),
            "양식을 저장했습니다.", "양식 저장");
    }

    // ── 공정별 레시피 ──────────────────────────────────────────────────────

    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/recipes")]
    public Task<ActionResult<MesSetupResultDto>> CreateRecipe(
        int productId, [FromBody] MesProductRecipeRequest request, CancellationToken ct)
        => RunAsync(() => _refData.CreateProductRecipeAsync(request.ToUpsert(productId), ct),
            "저장되었습니다.", "레시피 등록");

    [Authorize(Policy = "EditMes")]
    [HttpPut("{productId:int}/recipes/{assignmentId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UpdateRecipe(
        int productId, int assignmentId, [FromBody] MesProductRecipeRequest request, CancellationToken ct)
        => RunAsync(() => _refData.UpdateProductRecipeAsync(assignmentId, request.ToUpsert(productId), ct),
            "저장되었습니다.", "레시피 수정");

    [Authorize(Policy = "EditMes")]
    [HttpDelete("recipes/{assignmentId:int}")]
    public Task<ActionResult<MesSetupResultDto>> DeleteRecipe(int assignmentId, CancellationToken ct)
        => RunAsync(() => _refData.DeleteProductRecipeAsync(assignmentId, ct), "삭제했습니다.", "레시피 삭제");

    /// <summary>다른 세정코드의 레시피 설정을 그대로 가져온다. 이미 있는 것은 건너뛴다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/recipes/copy")]
    public Task<ActionResult<MesSetupResultDto>> CopyRecipes(
        int productId, [FromBody] MesCopyRequest request, CancellationToken ct)
        => RunCountAsync(
            () => _refData.CopyProductRecipesAsync(productId, (request.SourceCleaningCode ?? "").Trim(), ct),
            n => n == 0 ? "가져올 레시피가 없습니다(이미 있는 것은 건너뜁니다)." : $"레시피 {n}건을 가져왔습니다.",
            "레시피 복사");

    // ── 검사 파라미터 ──────────────────────────────────────────────────────

    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/parameters")]
    public Task<ActionResult<MesSetupResultDto>> CreateParameter(
        int productId, [FromBody] MesParameterRequest request, CancellationToken ct)
        => RunAsync(() => _refData.CreateParameterAsync(request.ToUpsert(productId), ct), "저장되었습니다.", "파라미터 등록");

    [Authorize(Policy = "EditMes")]
    [HttpPut("{productId:int}/parameters/{parameterId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UpdateParameter(
        int productId, int parameterId, [FromBody] MesParameterRequest request, CancellationToken ct)
        => RunAsync(() => _refData.UpdateParameterAsync(parameterId, request.ToUpsert(productId), ct),
            "저장되었습니다.", "파라미터 수정");

    [Authorize(Policy = "EditMes")]
    [HttpDelete("parameters/{parameterId:int}")]
    public Task<ActionResult<MesSetupResultDto>> DeleteParameter(int parameterId, CancellationToken ct)
        => RunAsync(() => _refData.DeleteParameterAsync(parameterId, ct), "삭제했습니다.", "파라미터 삭제");

    /// <summary>다른 세정코드의 검사 항목을 그대로 가져온다. 코드·OPER 가 겹치면 건너뛴다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{productId:int}/parameters/copy")]
    public Task<ActionResult<MesSetupResultDto>> CopyParameters(
        int productId, [FromBody] MesCopyRequest request, CancellationToken ct)
        => RunCountAsync(
            () => _refData.CopyParametersAsync(productId, (request.SourceCleaningCode ?? "").Trim(), ct),
            n => n == 0 ? "가져올 검사 항목이 없습니다(이미 있는 것은 건너뜁니다)." : $"검사 항목 {n}건을 가져왔습니다.",
            "파라미터 복사");

    private static ProductUpsertRequest Trim(ProductUpsertRequest r)
        => new((r.CleaningCode ?? "").Trim(), (r.ProductCode ?? "").Trim(), (r.ItemCode ?? "").Trim(),
               (r.ProductName ?? "").Trim(), r.SerialNumber?.Trim(), r.CustomerId, r.ItemCategory?.Trim());
}

public record MesProductReferenceDto(
    IReadOnlyList<CustomerDto> Customers,
    IReadOnlyList<LineOptionDto> Lines,
    IReadOnlyList<RecipeOptionDto> Recipes,
    IReadOnlyList<ParameterCatalogDto> ParameterCatalog,
    IReadOnlyList<OperOptionDto> Opers);

/// <summary><paramref name="TemplateFileName"/> 은 INSPECTION 폴더에서 찾을 성적서 양식 파일명.</summary>
public record MesProductDetailDto(
    IReadOnlyList<ProcessRouteOptionDto> AssignedFlows,
    IReadOnlyList<ProcessRouteOptionDto> AvailableFlows,
    int? DefaultLineId,
    IReadOnlyList<ProductRecipeDto> Recipes,
    IReadOnlyList<ParameterOptionDto> Parameters,
    string? TemplateFileName);

public record MesDefaultLineRequest(int? LineId);

/// <summary>
/// <paramref name="SourceCleaningCode"/> 를 비우면 그냥 새 제품을 만든다.
/// <paramref name="SourceProductId"/> 는 기본 LINE 을 가져오기 위한 것 — 세정코드만으로는 못 찾는다.
/// </summary>
public record MesCreateProductFromRequest(
    ProductUpsertRequest Product, string? SourceCleaningCode, int? SourceProductId);

public record MesCopyRequest(string? SourceCleaningCode);

/// <summary>공정(ProcessDefinitionId)은 고른 레시피의 OPER 로 서비스가 채운다 — 화면이 정하지 않는다.</summary>
public record MesProductRecipeRequest(int RecipeDefinitionId, decimal? MinValue, decimal? MaxValue, bool IsMain, bool IsActive)
{
    public ProductRecipeUpsertRequest ToUpsert(int productId)
        => new(productId, RecipeDefinitionId, MinValue, MaxValue, IsMain, IsActive);
}

public record MesParameterRequest(
    string Code, string Description, int ParameterType, string? Oper, int ValueCount,
    decimal? MinValue, decimal? MaxValue, string? Unit, bool IsActive, string? CertificateLabel)
{
    public ParameterUpsertRequest ToUpsert(int productId)
        => new((Code ?? "").Trim(), (Description ?? "").Trim(),
               (ProductionManagement.Domain.Enums.ParameterType)ParameterType,
               Oper?.Trim(), ValueCount <= 0 ? 1 : ValueCount, MinValue, MaxValue,
               Unit?.Trim(), IsActive, productId, CertificateLabel?.Trim());
}
