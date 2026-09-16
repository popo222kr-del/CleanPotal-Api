using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 세정 이력 조회 — 검사·공정 이력과 감사 로그.
///
/// 결과 표의 뒤쪽 열(입고·출고 파라미터 블록)은 제품마다 다르다. 어떤 열이 있는지도 서버가 함께
/// 내려주고(ColumnGroups) 화면은 그대로 그린다 — 화면이 열을 스스로 만들면 마스터에 없는 열이 생긴다.
/// </summary>
[ApiController]
[Route("api/mes/cleaning-history")]
[Authorize(Policy = "ViewMes")]
public class MesCleaningHistoryController : ControllerBase
{
    private readonly IProcessHistoryQueryRepository _history;
    private readonly IAuditLogQueryService _audit;
    private readonly ICustomerService _customers;

    public MesCleaningHistoryController(
        IProcessHistoryQueryRepository history,
        IAuditLogQueryService audit,
        ICustomerService customers)
    {
        _history = history;
        _audit = audit;
        _customers = customers;
    }

    /// <summary>업체 드롭다운을 채운다.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<MesCustomerOptionDto>>> Customers(CancellationToken ct)
        => Ok((await _customers.GetAllAsync(ct))
            .Select(c => new MesCustomerOptionDto(c.CustomerId, c.CustomerName))
            .ToList());

    /// <summary>
    /// 검사·공정 이력. 행과 그 행을 그리는 데 필요한 열 정의가 항상 한 쌍으로 온다
    /// — 둘이 따로 오면 화면이 어긋난 표를 그린다.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LotHistoryResultDto>> Search(
        [FromQuery] int? customerId,
        [FromQuery] string? cleaningCode,
        [FromQuery] string? itemCode,
        [FromQuery] string? serialNumber,
        [FromQuery] string? exportNumber,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken ct)
        => Ok(await _history.SearchLotHistoryAsync(new ProcessHistorySearchRequest(
            CustomerId: customerId,
            CleaningCode: Trimmed(cleaningCode),
            ItemCode: Trimmed(itemCode),
            ExportNumber: Trimmed(exportNumber),
            SerialNumber: Trimmed(serialNumber),
            DateFrom: dateFrom,
            DateTo: dateTo), ct));

    /// <summary>감사 로그 — 누가 무엇을 언제 바꿨는지.</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<AuditLogDto>>> Audit(
        [FromQuery] string? keyword, [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo,
        CancellationToken ct)
        => Ok(await _audit.SearchAsync(new AuditLogSearchRequest(Trimmed(keyword), dateFrom, dateTo), ct));

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public record MesCustomerOptionDto(int CustomerId, string CustomerName);
