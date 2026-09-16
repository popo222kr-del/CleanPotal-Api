using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Enums;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 셋업 — 마스터 데이터 관리(업체 · 공정 · 제품 · 단가).
///
/// 여기서는 포털 권한(ViewMes/EditMes) 위에 <b>MES 자체 세부 권한</b>이 한 겹 더 있다.
/// 마스터는 한 번 잘못 바꾸면 이후 모든 LOT 이 영향을 받아서, 누가 무엇을 고칠 수 있는지를
/// 화면 단위보다 잘게 나눠 놓은 것이다(PermissionCode). 판정은 MES 업무 계층이 하고,
/// 쓰기 메서드는 서비스 안에서 한 번 더 막는다.
/// </summary>
[ApiController]
[Route("api/mes/setup")]
[Authorize(Policy = "ViewMes")]
public class MesSetupController : ControllerBase
{
    // Microsoft.AspNetCore.Authorization 에도 같은 이름이 있어 전체 이름을 쓴다.
    private readonly ProductionManagement.Application.Interfaces.IAuthorizationService _authorization;
    private readonly ICustomerService _customers;
    private readonly IProcessDefinitionService _processes;
    private readonly IProductReferenceDataService _refData;
    private readonly ILogger<MesSetupController> _log;

    public MesSetupController(
        ProductionManagement.Application.Interfaces.IAuthorizationService authorization,
        ICustomerService customers,
        IProcessDefinitionService processes,
        IProductReferenceDataService refData,
        ILogger<MesSetupController> log)
    {
        _authorization = authorization;
        _customers = customers;
        _processes = processes;
        _refData = refData;
        _log = log;
    }

    /// <summary>이 사람에게 어떤 셋업 탭을 보여 줄지. 권한 조회가 실패하면 전부 막힌 것으로 본다.</summary>
    [HttpGet("permissions")]
    public async Task<ActionResult<MesSetupPermissionsDto>> Permissions(CancellationToken ct)
    {
        try
        {
            var p = await _authorization.GetCurrentUserPermissionsAsync(ct);
            return Ok(new MesSetupPermissionsDto(
                p.IsAdmin,
                p.Has(PermissionCode.AdminProduct),
                p.Has(PermissionCode.AdminCustomer),
                p.Has(PermissionCode.AdminProcess)));
        }
        catch (Exception ex)
        {
            // 화면이 어중간하게 열리는 것보다 아예 안 열리는 편이 낫다(MES 화면과 같은 판단).
            _log.LogError(ex, "MES 셋업 권한 조회 실패");
            return Ok(new MesSetupPermissionsDto(false, false, false, false));
        }
    }

    // ── 업체 관리 ──────────────────────────────────────────────────────────

    /// <summary>업체 목록과 LINE 목록. LINE 은 업체가 속한 대분류다.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<MesCustomerSetupDto>> Customers(CancellationToken ct)
        => Ok(new MesCustomerSetupDto(
            await _customers.GetAllAsync(ct),
            await _refData.GetLinesAsync(ct)));

    /// <summary>업체 등록.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("customers")]
    public Task<ActionResult<MesSetupResultDto>> CreateCustomer(
        [FromBody] CustomerUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _customers.CreateAsync(Trim(request), ct), "저장되었습니다.", "업체 등록");

    /// <summary>업체 수정.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPut("customers/{customerId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UpdateCustomer(
        int customerId, [FromBody] CustomerUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _customers.UpdateAsync(customerId, Trim(request), ct), "저장되었습니다.", "업체 수정");

    /// <summary>
    /// 업체 중지·활성화. <b>지우지 않는다</b> — 과거 LOT 과 제품이 이 업체를 참조하고 있어서,
    /// 지우면 그 기록들이 가리키는 곳이 사라진다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("customers/{customerId:int}/active")]
    public Task<ActionResult<MesSetupResultDto>> SetCustomerActive(
        int customerId, [FromBody] MesActiveRequest request, CancellationToken ct)
        => RunAsync(() => _customers.SetActiveAsync(customerId, request.IsActive, ct),
            request.IsActive ? "활성화했습니다." : "중지했습니다.", "업체 상태 변경");

    // ── 공정 관리 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 공정 목록(OPER 순)과 공정 플로우 목록. 플로우는 공정을 순서대로 엮은 것이라 둘을 같이 본다.
    /// </summary>
    [HttpGet("processes")]
    public async Task<ActionResult<MesProcessSetupDto>> Processes(CancellationToken ct)
        => Ok(new MesProcessSetupDto(
            (await _processes.GetAllAsync(ct)).OrderBy(p => p.OperCode).ToList(),
            await _processes.GetAllRouteDetailsAsync(ct)));

    [Authorize(Policy = "EditMes")]
    [HttpPost("processes")]
    public Task<ActionResult<MesSetupResultDto>> CreateProcess(
        [FromBody] ProcessDefinitionUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _processes.CreateAsync(Trim(request), ct), "저장되었습니다.", "공정 등록");

    [Authorize(Policy = "EditMes")]
    [HttpPut("processes/{processDefinitionId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UpdateProcess(
        int processDefinitionId, [FromBody] ProcessDefinitionUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _processes.UpdateAsync(processDefinitionId, Trim(request), ct), "저장되었습니다.", "공정 수정");

    /// <summary>공정 중지·활성화. 업체와 같은 이유로 지우지 않는다(지나간 이력이 이 공정을 가리킨다).</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("processes/{processDefinitionId:int}/active")]
    public Task<ActionResult<MesSetupResultDto>> SetProcessActive(
        int processDefinitionId, [FromBody] MesActiveRequest request, CancellationToken ct)
        => RunAsync(() => _processes.SetActiveAsync(processDefinitionId, request.IsActive, ct),
            request.IsActive ? "활성화했습니다." : "중지했습니다.", "공정 상태 변경");

    /// <summary>
    /// 공정 플로우 등록. 같은 공정이 한 플로우에 여러 번 들어갈 수 있다
    /// (LASER 경로의 세정·건조 반복) — 그래서 순서는 집합이 아니라 목록이다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("routes")]
    public Task<ActionResult<MesSetupResultDto>> CreateRoute(
        [FromBody] ProcessRouteUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _processes.CreateRouteAsync(Trim(request), ct), "저장되었습니다.", "플로우 등록");

    [Authorize(Policy = "EditMes")]
    [HttpPut("routes/{processRouteId:int}")]
    public Task<ActionResult<MesSetupResultDto>> UpdateRoute(
        int processRouteId, [FromBody] ProcessRouteUpsertRequest request, CancellationToken ct)
        => RunAsync(() => _processes.UpdateRouteAsync(processRouteId, Trim(request), ct), "저장되었습니다.", "플로우 수정");

    [Authorize(Policy = "EditMes")]
    [HttpPost("routes/{processRouteId:int}/active")]
    public Task<ActionResult<MesSetupResultDto>> SetRouteActive(
        int processRouteId, [FromBody] MesActiveRequest request, CancellationToken ct)
        => RunAsync(() => _processes.SetRouteActiveAsync(processRouteId, request.IsActive, ct),
            request.IsActive ? "활성화했습니다." : "중지했습니다.", "플로우 상태 변경");

    // ── 내부 ───────────────────────────────────────────────────────────────

    private static ProcessDefinitionUpsertRequest Trim(ProcessDefinitionUpsertRequest r)
        => new((r.ProcessCode ?? "").Trim(), (r.ProcessName ?? "").Trim());

    private static ProcessRouteUpsertRequest Trim(ProcessRouteUpsertRequest r)
        => new((r.RouteCode ?? "").Trim(), (r.RouteName ?? "").Trim(),
               r.ProcessDefinitionIds ?? Array.Empty<int>());

    private static CustomerUpsertRequest Trim(CustomerUpsertRequest r)
        => new((r.CustomerCode ?? "").Trim(), (r.CustomerName ?? "").Trim(), (r.ExportPrefix ?? "").Trim(), r.LineDefinitionId);

    /// <summary>
    /// 마스터 저장 공통. 권한 부족·규칙 위반은 사유를 그대로 보여 주고(고칠 수 있는 정보다),
    /// 그 밖의 예외는 원문을 감춘다.
    /// </summary>
    private async Task<ActionResult<MesSetupResultDto>> RunAsync(Func<Task> work, string okMessage, string label)
    {
        try
        {
            await work();
            return Ok(new MesSetupResultDto(true, okMessage));
        }
        catch (UnauthorizedException ex)
        {
            return Ok(new MesSetupResultDto(false, ex.Message));
        }
        catch (ValidationException ex)
        {
            return Ok(new MesSetupResultDto(false, string.Join(" / ", ex.Errors)));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new MesSetupResultDto(false, ex.Message));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES {Label} 실패", label);
            return Ok(new MesSetupResultDto(false, $"{label} 중 문제가 발생했습니다. 관리자에게 문의하세요."));
        }
    }
}

/// <summary>보여 줄 셋업 탭. MES 세부 권한(PermissionCode)을 탭 단위로 접어서 내려준다.</summary>
public record MesSetupPermissionsDto(bool IsAdmin, bool Product, bool Customer, bool Process);

public record MesCustomerSetupDto(IReadOnlyList<CustomerDto> Customers, IReadOnlyList<LineOptionDto> Lines);

public record MesProcessSetupDto(
    IReadOnlyList<ProcessDefinitionDto> Processes,
    IReadOnlyList<ProcessRouteDetailDto> Routes);

public record MesActiveRequest(bool IsActive);

public record MesSetupResultDto(bool Success, string Message);
