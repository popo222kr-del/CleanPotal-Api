using CleanPotal.Core;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 업체 관리의 업체를 MES 업체로 한 번에 올린다.
///
/// 두 곳의 업체 자료는 뿌리가 달라 자동으로 이어지지 않는다 — 포털 업체는 연락처·주소를 들고 있고,
/// MES 업체는 전산등록·LOT·반출번호가 참조한다. 한 건씩 이어 두는 것은 업체 관리 창에서 하고,
/// 여기서는 <b>아직 MES 에 없는 업체 전부</b>를 초안 코드와 함께 내려준 다음, 사람이 표에서 고친 값으로 등록한다.
///
/// 이름이 같은 MES 업체가 이미 있으면 새로 만들지 않고 이어 붙이기만 한다 — 같은 업체가 MES 에
/// 둘로 생기면 과거 LOT 이 어느 쪽에 붙었는지 알 수 없게 된다.
/// </summary>
[ApiController]
[Route("api/vendor/mes-bulk")]
[Authorize(Policy = "ViewVendors")]
public class VendorMesController : MesSetupControllerBase
{
    private readonly CleanPotalDbContext _db;
    private readonly ICustomerService _customers;
    private readonly IProductReferenceDataService _refData;
    private readonly ILogger<VendorMesController> _log;

    public VendorMesController(
        CleanPotalDbContext db,
        ICustomerService customers,
        IProductReferenceDataService refData,
        ILogger<VendorMesController> log) : base(log)
    {
        _db = db;
        _customers = customers;
        _refData = refData;
        _log = log;
    }

    /// <summary>등록할 목록의 초안. 저장하지 않는다 — 화면이 표로 보여 주고 사람이 고친다.</summary>
    [HttpGet]
    [Authorize(Policy = "ViewMes")]
    public async Task<ActionResult<VendorMesBulkPreviewDto>> Preview(CancellationToken ct)
    {
        var vendors = await _db.Vendors.AsNoTracking().ToListAsync(ct);
        var customers = await _customers.GetAllAsync(ct);
        var lines = await _refData.GetLinesAsync(ct);

        // 이름으로 찾을 때는 공백과 대소문자를 무시한다 — "삼성 전자" 와 "삼성전자" 는 한 업체다.
        var byName = new Dictionary<string, CustomerDto>();
        foreach (var c in customers) byName.TryAdd(NameKey(c.CustomerName), c);

        var takenCodes = customers.Select(c => c.CustomerCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenPrefixes = customers.Select(c => c.ExportPrefix).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pending = vendors
            .Where(v => v.MesCustomerId is null)
            .OrderBy(v => v.VendorName, VendorMesCodes.NameOrder)
            .ToList();

        var number = 0;
        var rows = new List<VendorMesBulkRowDto>(pending.Count);
        foreach (var v in pending)
        {
            if (byName.TryGetValue(NameKey(v.VendorName), out var existing))
            {
                // 이미 MES 에 있다 — 코드를 새로 매기지 않고 그 업체에 잇기만 한다.
                rows.Add(new VendorMesBulkRowDto(
                    v.Id, v.VendorName, existing.CustomerCode, existing.ExportPrefix,
                    existing.LineDefinitionId, existing.CustomerId, existing.CustomerName));
                continue;
            }

            rows.Add(new VendorMesBulkRowDto(
                v.Id, v.VendorName,
                VendorMesCodes.NextCode(ref number, takenCodes),
                VendorMesCodes.UniquePrefix(v.VendorName, takenPrefixes),
                null, null, null));
        }

        var linked = vendors.Count(v => v.MesCustomerId is not null);
        return Ok(new VendorMesBulkPreviewDto(rows, linked, vendors.Count, lines));
    }

    /// <summary>표에서 고친 값으로 등록한다. 한 줄이 실패해도 나머지는 그대로 올라간다.</summary>
    [HttpPost]
    [Authorize(Policy = "EditVendors")]
    [Authorize(Policy = "EditMes")]
    public async Task<ActionResult<VendorMesBulkResultDto>> Register(
        [FromBody] VendorMesBulkRequest request, CancellationToken ct)
    {
        var items = request.Items ?? [];
        if (items.Count == 0)
            return Ok(new VendorMesBulkResultDto(false, "등록할 업체가 없습니다.", 0, 0, []));

        var ids = items.Select(i => i.VendorId).Distinct().ToList();
        var vendors = await _db.Vendors
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var created = 0;
        var linked = 0;
        var failures = new List<VendorMesBulkFailureDto>();

        foreach (var item in items)
        {
            if (!vendors.TryGetValue(item.VendorId, out var vendor))
            {
                failures.Add(new VendorMesBulkFailureDto(item.VendorId, "", "업체를 찾을 수 없습니다."));
                continue;
            }

            // 이미 이어져 있으면 건너뛴다 — 화면을 열어 둔 사이 다른 사람이 이어 뒀을 수 있다.
            if (vendor.MesCustomerId is not null) continue;

            try
            {
                if (item.MesCustomerId is { } existingId)
                {
                    vendor.MesCustomerId = existingId;
                    linked++;
                }
                else
                {
                    var made = await _customers.CreateAsync(new CustomerUpsertRequest(
                        (item.CustomerCode ?? "").Trim(),
                        vendor.VendorName,
                        (item.ExportPrefix ?? "").Trim(),
                        item.LineDefinitionId), ct);
                    vendor.MesCustomerId = made.CustomerId;
                    created++;
                }
            }
            catch (UnauthorizedException ex)
            {
                // 권한이 없으면 뒤에 오는 줄도 모두 같은 이유로 막힌다 — 여기서 멈춘다.
                await _db.SaveChangesAsync(ct);
                return Ok(new VendorMesBulkResultDto(false, ex.Message, created, linked, failures));
            }
            catch (ValidationException ex)
            {
                failures.Add(new VendorMesBulkFailureDto(vendor.Id, vendor.VendorName, string.Join(" / ", ex.Errors)));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "MES 업체 일괄 등록 실패 — {Vendor}", vendor.VendorName);
                failures.Add(new VendorMesBulkFailureDto(vendor.Id, vendor.VendorName, "등록 중 문제가 발생했습니다."));
            }
        }

        await _db.SaveChangesAsync(ct);

        var message = failures.Count == 0
            ? $"{created}개를 새로 등록하고 {linked}개를 이어 붙였습니다."
            : $"{created}개 등록 · {linked}개 연결 · {failures.Count}개 실패했습니다.";
        return Ok(new VendorMesBulkResultDto(failures.Count == 0, message, created, linked, failures));
    }

    private static string NameKey(string? name)
        => new string((name ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}

/// <summary>등록 초안 한 줄. MesCustomerId 가 있으면 새로 만들지 않고 그 업체에 잇는다.</summary>
public record VendorMesBulkRowDto(
    int VendorId, string VendorName, string CustomerCode, string ExportPrefix,
    int? LineDefinitionId, int? MesCustomerId, string? MesCustomerName);

public record VendorMesBulkPreviewDto(
    IReadOnlyList<VendorMesBulkRowDto> Rows, int LinkedCount, int TotalCount,
    IReadOnlyList<LineOptionDto> Lines);

public record VendorMesBulkItem(int VendorId, string? CustomerCode, string? ExportPrefix, int? LineDefinitionId, int? MesCustomerId);

public record VendorMesBulkRequest(IReadOnlyList<VendorMesBulkItem>? Items);

public record VendorMesBulkFailureDto(int VendorId, string VendorName, string Message);

public record VendorMesBulkResultDto(
    bool Success, string Message, int Created, int Linked, IReadOnlyList<VendorMesBulkFailureDto> Failures);
