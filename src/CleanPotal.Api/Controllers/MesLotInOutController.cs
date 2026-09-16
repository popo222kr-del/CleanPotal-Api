using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Services;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 입·출고 현황 조회와 Batch(묶기·해제).
///
/// 매트릭스 집계·필터·드릴다운 규칙은 MES 데스크톱판과 공유하는 <see cref="WipMatrixBuilder"/> 가 원본이다.
/// 화면이 셀을 누르면 <b>서버가 준 셀 객체를 그대로 되돌려 보내고</b> 서버가 다시 걸러 준다 —
/// 어떤 칸이 어떤 조건인지를 화면이 해석하기 시작하면 두 곳이 갈라진다.
/// </summary>
[ApiController]
[Route("api/mes/lot-inout")]
[Authorize(Policy = "ViewMes")]
public class MesLotInOutController : ControllerBase
{
    // 매트릭스는 기간과 무관한 전체 LOT 을 집계한다. 하단 목록만 기간으로 좁힌다(MES 화면과 같다).
    private const int MatrixTake = 3000;
    private const int DetailTake = 500;

    private readonly ILotService _lots;
    private readonly IProcessDefinitionService _processes;
    private readonly IProductService _products;
    private readonly ILogger<MesLotInOutController> _log;

    public MesLotInOutController(
        ILotService lots,
        IProcessDefinitionService processes,
        IProductService products,
        ILogger<MesLotInOutController> log)
    {
        _lots = lots;
        _processes = processes;
        _products = products;
        _log = log;
    }

    /// <summary>화면에 처음 들어왔을 때. MES 와 같이 자료는 띄우지 않고 업체 목록만 채운다.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<MesInOutOptionsDto>> Options(CancellationToken ct)
    {
        var all = (await _lots.SearchAsync(new LotSearchRequest(Take: MatrixTake), ct)).Items;
        return Ok(new MesInOutOptionsDto(
            WipMatrixBuilder.CustomerOptions(all),
            WipMatrixBuilder.StageColumnTitles));
    }

    /// <summary>재공현황 매트릭스 + 하단 LOT 목록.</summary>
    [HttpGet]
    public async Task<ActionResult<MesInOutResultDto>> Search(
        [FromQuery] string? customer, [FromQuery] string? cleaningCode, [FromQuery] string? serialNumber,
        [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo, CancellationToken ct)
    {
        var filter = Filter(customer, cleaningCode, serialNumber, dateFrom, dateTo);
        var (all, operCodeMap, categoryMap) = await SourceAsync(ct);

        // 하단 목록은 기간으로 좁히고, 매트릭스는 기간 무관 전체를 집계한다.
        var detail = (await _lots.SearchAsync(
            new LotSearchRequest(DateFrom: filter.DateFrom, DateTo: filter.DateTo, Take: DetailTake), ct)).Items;

        var matrixSource = WipMatrixBuilder.ApplyFilters(all, filter);
        var matrix = WipMatrixBuilder.Build(matrixSource, filter, operCodeMap, categoryMap);

        return Ok(new MesInOutResultDto(
            WipMatrixBuilder.CustomerOptions(all),
            WipMatrixBuilder.StageColumnTitles,
            matrix.Customers,
            matrix.Rows,
            WipMatrixBuilder.ApplyFilters(detail, filter),
            WipMatrixBuilder.HasInOutFilter(filter)));
    }

    /// <summary>재공현황 칸(또는 MAT DESC·MAT ID)을 눌렀을 때 하단 목록을 그 조건으로 좁힌다.</summary>
    [HttpPost("drill")]
    public async Task<ActionResult<MesInOutDrillDto>> Drill([FromBody] MesInOutDrillRequest request, CancellationToken ct)
    {
        var filter = Filter(request.Customer, request.CleaningCode, request.SerialNumber, request.DateFrom, request.DateTo);
        var (all, operCodeMap, _) = await SourceAsync(ct);
        var source = WipMatrixBuilder.ApplyFilters(all, filter);

        var lots = WipMatrixBuilder.DrillDown(source, request.Target, operCodeMap);
        return Ok(new MesInOutDrillDto(lots, WipMatrixBuilder.DrillDownMessage(request.Target, lots.Count)));
    }

    /// <summary>입고현황·출고현황의 업체별 칸을 눌렀을 때.</summary>
    [HttpPost("drill-inout")]
    public async Task<ActionResult<MesInOutDrillDto>> DrillInOut(
        [FromBody] MesInOutCellDrillRequest request, CancellationToken ct)
    {
        var filter = Filter(request.Customer, request.CleaningCode, request.SerialNumber, request.DateFrom, request.DateTo);
        var (all, _, _) = await SourceAsync(ct);
        var source = WipMatrixBuilder.ApplyFilters(all, filter);

        var lots = WipMatrixBuilder.DrillInOut(source, request.Cell, filter);
        return Ok(new MesInOutDrillDto(lots, WipMatrixBuilder.DrillInOutMessage(request.Cell, lots.Count)));
    }

    // ── Batch ──────────────────────────────────────────────────────────────

    /// <summary>Batch 화면 목록. LOT번호·S/N·제품명으로 찾는다.</summary>
    [HttpGet("/api/mes/batch")]
    public async Task<ActionResult<IReadOnlyList<LotListItemDto>>> BatchList(
        [FromQuery] string? keyword, CancellationToken ct)
        => Ok((await _lots.SearchAsync(
            new LotSearchRequest(string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(), Take: 200), ct)).Items);

    /// <summary>고른 LOT 을 하나로 묶는다(둘 이상이어야 한다).</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("/api/mes/batch")]
    public Task<ActionResult<MesBatchResultDto>> Batch([FromBody] MesBatchRequest request, CancellationToken ct)
        => RunBatchAsync(() => _lots.BatchAsync(request.LotIds, ct), "묶기 완료", "Batch 묶기");

    /// <summary>고른 LOT 의 묶음을 푼다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("/api/mes/batch/unbatch")]
    public Task<ActionResult<MesBatchResultDto>> Unbatch([FromBody] MesBatchRequest request, CancellationToken ct)
        => RunBatchAsync(() => _lots.UnbatchAsync(request.LotIds, ct), "해제 완료", "Batch 해제");

    // ── 내부 ───────────────────────────────────────────────────────────────

    private async Task<ActionResult<MesBatchResultDto>> RunBatchAsync(Func<Task> work, string okMessage, string label)
    {
        try
        {
            await work();
            return Ok(new MesBatchResultDto(true, okMessage));
        }
        catch (ValidationException ex)
        {
            return Ok(new MesBatchResultDto(false, string.Join(" / ", ex.Errors)));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new MesBatchResultDto(false, ex.Message));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES {Label} 실패", label);
            return Ok(new MesBatchResultDto(false, $"{label} 중 문제가 발생했습니다. 관리자에게 문의하세요."));
        }
    }

    private static WipMatrixFilter Filter(
        string? customer, string? cleaningCode, string? serialNumber, DateTime? dateFrom, DateTime? dateTo)
        => new(string.IsNullOrWhiteSpace(customer) ? null : customer.Trim(),
               string.IsNullOrWhiteSpace(cleaningCode) ? null : cleaningCode.Trim(),
               string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim(),
               dateFrom, dateTo);

    private async Task<(IReadOnlyList<LotListItemDto> All,
                        IReadOnlyDictionary<string, int> OperCodeMap,
                        IReadOnlyDictionary<string, string?> CategoryMap)> SourceAsync(CancellationToken ct)
    {
        var all = (await _lots.SearchAsync(new LotSearchRequest(Take: MatrixTake), ct)).Items;
        var operCodeMap = WipMatrixBuilder.CreateOperCodeMap(await _processes.GetAllAsync(ct));
        var categoryMap = WipMatrixBuilder.CreateCategoryMap(await _products.GetAllAsync(ct));
        return (all, operCodeMap, categoryMap);
    }
}

public record MesInOutOptionsDto(IReadOnlyList<string> CustomerOptions, IReadOnlyList<string> StageTitles);

/// <summary><paramref name="InOutVisible"/>: 업체·세정코드·S/N 중 하나라도 넣었을 때만 입고·출고 그룹이 보인다.</summary>
public record MesInOutResultDto(
    IReadOnlyList<string> CustomerOptions,
    IReadOnlyList<string> StageTitles,
    IReadOnlyList<string> Customers,
    IReadOnlyList<WipMatrixRowDto> Rows,
    IReadOnlyList<LotListItemDto> Lots,
    bool InOutVisible);

/// <summary>화면이 받은 셀 객체를 그대로 돌려보낸다 — 조건 해석은 서버가 한다.</summary>
public record MesInOutDrillRequest(
    string? Customer, string? CleaningCode, string? SerialNumber,
    DateTime? DateFrom, DateTime? DateTo, MatrixDrillTarget Target);

public record MesInOutCellDrillRequest(
    string? Customer, string? CleaningCode, string? SerialNumber,
    DateTime? DateFrom, DateTime? DateTo, MatrixInOutCell Cell);

public record MesInOutDrillDto(IReadOnlyList<LotListItemDto> Lots, string Message);

public record MesBatchRequest(IReadOnlyList<int> LotIds);

public record MesBatchResultDto(bool Success, string Message);
