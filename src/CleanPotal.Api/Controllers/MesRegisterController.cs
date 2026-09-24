using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 전산등록(CREATE) — 여러 행을 표에 채워 한 번에 등록한다.
///
/// 화면이 보내는 것은 사람이 친 값(세정코드 등)뿐이고, 제품·업체·공정 플로우를 실제로 고르는 것은
/// 서버다. 화면이 ID 를 골라 보내면 없는 조합을 만들어 보낼 수 있고, 무엇보다 같은 규칙이 두 군데
/// 생긴다.
/// </summary>
[ApiController]
[Route("api/mes/register")]
[Authorize(Policy = "ViewMes")]
public class MesRegisterController : ControllerBase
{
    // LOT 번호 생성에 쓰는 2자리 코드. MES 화면과 같이 SS 고정이다.
    private const string LotCode = "SS";
    // 제품에 플로우가 배정돼 있지 않을 때 쓰는 기본 플로우의 코드.
    private const string FallbackRouteCode = "STANDARD";

    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly IProcessDefinitionService _processes;
    private readonly IProductFlowService _flows;
    private readonly IRegistrationService _registrations;
    private readonly ILogger<MesRegisterController> _log;

    public MesRegisterController(
        IProductService products,
        ICustomerService customers,
        IProcessDefinitionService processes,
        IProductFlowService flows,
        IRegistrationService registrations,
        ILogger<MesRegisterController> log)
    {
        _products = products;
        _customers = customers;
        _processes = processes;
        _flows = flows;
        _registrations = registrations;
        _log = log;
    }

    /// <summary>
    /// 세정코드로 찾을 수 있는 제품 목록. 화면이 세정코드를 칠 때마다 서버에 묻지 않고
    /// 바로 "품목명 확인" 을 보여주려고 한 번에 받아 둔다.
    /// </summary>
    [HttpGet("reference")]
    public async Task<ActionResult<IReadOnlyList<MesRegisterProductDto>>> Reference(CancellationToken ct)
    {
        var map = await LookupAsync(ct);
        return Ok(map.Values.OrderBy(p => p.CleaningCode, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// 여러 행을 등록한다. <b>행마다 독립</b>이라 한 행이 실패해도 나머지는 계속 등록된다
    /// (한 건 때문에 통째로 되돌리면 작업자가 스무 행을 다시 친다).
    /// 그래서 실패해도 200 이고, 어느 행이 왜 안 됐는지는 결과 안에 들어 있다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<MesRegisterRowResultDto>>> Create(
        [FromBody] MesRegisterRequest request, CancellationToken ct)
    {
        var map = await LookupAsync(ct);
        var results = new List<MesRegisterRowResultDto>();

        for (var i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var code = (row.CleaningCode ?? "").Trim();

            if (code.Length == 0 || !map.TryGetValue(code, out var product))
            {
                results.Add(new MesRegisterRowResultDto(i, false, "세정코드를 확인하세요.", null, null));
                continue;
            }
            if (product.CustomerId is null)
            {
                results.Add(new MesRegisterRowResultDto(i, false, "이 제품의 업체가 비활성이거나 없습니다.", null, null));
                continue;
            }
            if (product.ProcessRouteId is null)
            {
                results.Add(new MesRegisterRowResultDto(i, false, "배정된 공정 플로우가 없습니다.", null, null));
                continue;
            }

            var create = new RegistrationCreateRequest(
                product.CustomerId.Value,
                product.ProductId,
                product.ProcessRouteId.Value,
                (row.Line ?? "").Trim(),
                (row.ProcessLabel ?? "").Trim(),
                row.Quantity < 1 ? 1 : row.Quantity,
                row.ShipDate?.Date ?? DateTime.Today,
                LotCode,
                // 비우면 자동 채번, 채우면 그 값을 쓴다(S/N 은 수량 1 일 때만 — 서비스 규칙).
                string.IsNullOrWhiteSpace(row.ExportNumber) ? null : row.ExportNumber!.Trim(),
                string.IsNullOrWhiteSpace(row.SerialNumber) ? null : row.SerialNumber!.Trim(),
                // 업체명을 비워 보내면 제품의 업체 이름을 기본값으로 쓴다(화면의 기본값과 같은 규칙).
                string.IsNullOrWhiteSpace(row.PmEquipmentName) ? (product.CustomerName ?? "") : row.PmEquipmentName!.Trim(),
                (row.TeamName ?? "").Trim(),
                (row.OrderNumber ?? "").Trim());

            try
            {
                var result = await _registrations.CreateAsync(create, ct);
                results.Add(new MesRegisterRowResultDto(
                    i, true,
                    $"반출번호 {result.ExportNumber} - LOT {result.CreatedLots.Count}건 등록완료",
                    result.CreatedLots.FirstOrDefault()?.LotNumber,
                    result.ExportNumber));
            }
            catch (ProductionManagement.Application.Exceptions.ValidationException ex)
            {
                // 중지된 품목, S/N 규칙 등 작업자가 고칠 수 있는 입력 문제 — 서비스가 준 사유를 그대로 보여 준다.
                // 예전에는 아래 일반 오류로 떨어져 "관리자에게 문의하세요" 만 떴다.
                var reason = ex.Errors.Count > 0 ? string.Join(" / ", ex.Errors) : ex.Message;
                _log.LogInformation("MES 전산등록 거절 (행 {Row}, 세정코드 {Code}): {Reason}", i, code, reason);
                results.Add(new MesRegisterRowResultDto(i, false, reason, null, null));
            }
            catch (Exception ex)
            {
                // 업무 규칙 위반(중복 반출번호 등)은 그대로 알려 주고, 그 밖의 오류는 원문을 감춘다
                // — DB 오류 문구가 작업자 화면에 그대로 뜨면 알아볼 수도, 고칠 수도 없다.
                _log.LogError(ex, "MES 전산등록 실패 (행 {Row}, 세정코드 {Code})", i, code);
                results.Add(new MesRegisterRowResultDto(
                    i, false,
                    ex is InvalidOperationException or ArgumentException
                        ? ex.Message
                        : "등록 중 문제가 발생했습니다. 관리자에게 문의하세요.",
                    null, null));
            }
        }

        return Ok(results);
    }

    /// <summary>세정코드 → 제품·업체·공정 플로우. 화면과 등록이 같은 표를 보게 한 곳에서 만든다.</summary>
    private async Task<Dictionary<string, MesRegisterProductDto>> LookupAsync(CancellationToken ct)
    {
        var customers = (await _customers.GetAllAsync(ct))
            .Where(c => c.IsActive)
            .ToDictionary(c => c.CustomerId);
        var products = await _products.GetAllAsync(ct);
        var routes = await _processes.GetAllRoutesAsync(ct);
        var fallback = routes.FirstOrDefault(r => r.RouteCode == FallbackRouteCode) ?? routes.FirstOrDefault();
        var routeMap = await _flows.GetProductRouteMapAsync(ct);

        var map = new Dictionary<string, MesRegisterProductDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in products.Where(p => p.IsActive))
        {
            var code = (p.CleaningCode ?? "").Trim();
            // 세정코드는 제품을 가리키는 유일 키다. 비었거나 겹치면 어느 제품인지 정할 수 없어 건너뛴다.
            if (code.Length == 0 || map.ContainsKey(code)) continue;

            var customer = customers.TryGetValue(p.CustomerId, out var c) ? c : null;
            var route = routeMap.TryGetValue(p.ProductId, out var r) ? r : fallback;
            map[code] = new MesRegisterProductDto(
                p.ProductId, code, p.ProductName,
                customer?.CustomerId, customer?.CustomerName,
                route?.ProcessRouteId, route?.RouteName);
        }
        return map;
    }
}

/// <summary>세정코드로 찾은 제품 한 건. 업체·플로우가 null 이면 그 이유가 곧 등록 불가 사유다.</summary>
public record MesRegisterProductDto(
    int ProductId,
    string CleaningCode,
    string ProductName,
    int? CustomerId,
    string? CustomerName,
    int? ProcessRouteId,
    string? RouteName);

public record MesRegisterRequest(IReadOnlyList<MesRegisterRowDto> Rows);

/// <summary>화면의 표 한 줄. 사람이 친 값만 담는다 — 제품·업체·플로우는 서버가 고른다.</summary>
public record MesRegisterRowDto(
    DateTime? ShipDate,
    string? ExportNumber,
    string? SerialNumber,
    string? CleaningCode,
    string? Line,
    string? ProcessLabel,
    string? PmEquipmentName,
    string? TeamName,
    int Quantity,
    string? OrderNumber);

/// <summary><paramref name="RowIndex"/> 는 보낸 순서 그대로다 — 화면이 어느 줄이었는지 되찾는다.</summary>
public record MesRegisterRowResultDto(
    int RowIndex,
    bool Success,
    string Message,
    string? FirstLotNumber,
    string? ExportNumber);
