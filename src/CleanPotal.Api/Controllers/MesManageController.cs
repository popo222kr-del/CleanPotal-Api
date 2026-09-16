using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 공정관리·조회 화면들 — HOLD 관리 · 재작업 관리 · 성적서 조회 · 이력 삭제.
/// 넷 다 목록 하나에 동작 하나뿐이라 컨트롤러를 따로 두지 않고 모았다.
/// </summary>
[ApiController]
[Route("api/mes")]
[Authorize(Policy = "ViewMes")]
public class MesManageController : ControllerBase
{
    private readonly IHoldService _holds;
    private readonly IReworkService _reworks;
    private readonly IDocumentService _documents;
    private readonly ILotService _lots;
    private readonly IProcessHistoryQueryRepository _historyQuery;
    private readonly IProcessHistoryVoidService _void;
    private readonly ILogger<MesManageController> _log;

    public MesManageController(
        IHoldService holds,
        IReworkService reworks,
        IDocumentService documents,
        ILotService lots,
        IProcessHistoryQueryRepository historyQuery,
        IProcessHistoryVoidService voidService,
        ILogger<MesManageController> log)
    {
        _holds = holds;
        _reworks = reworks;
        _documents = documents;
        _lots = lots;
        _historyQuery = historyQuery;
        _void = voidService;
        _log = log;
    }

    /// <summary>
    /// HOLD 이력. 해제는 여기서 하지 않는다 — LOT 이 있는 OPER 화면의 Release TRAN 으로 한다
    /// (해제도 공정 이력에 남아야 하므로 목록 화면이 몰래 풀면 안 된다).
    /// </summary>
    [HttpGet("holds")]
    public async Task<ActionResult<IReadOnlyList<HoldDto>>> Holds([FromQuery] bool onlyOpen = true, CancellationToken ct = default)
    {
        var all = await _holds.GetAllAsync(ct);
        return Ok(onlyOpen ? all.Where(h => !h.IsReleased).ToList() : all.ToList());
    }

    /// <summary>재작업 결정 이력.</summary>
    [HttpGet("reworks")]
    public async Task<ActionResult<IReadOnlyList<ReworkDto>>> Reworks(CancellationToken ct)
        => Ok(await _reworks.GetAllAsync(ct));

    /// <summary>
    /// 성적서 목록. 검색어를 주면 그 말로 찾은 LOT 도 같이 내려준다 —
    /// 새 버전을 올릴 때 "어느 LOT 에" 를 고르는 목록이다.
    /// </summary>
    [HttpGet("certificates")]
    public async Task<ActionResult<MesCertificateSearchDto>> Certificates(
        [FromQuery] string? keyword, CancellationToken ct)
    {
        var documents = await _documents.SearchAsync(keyword, ct);
        var lots = string.IsNullOrWhiteSpace(keyword)
            ? Array.Empty<MesCertificateLotDto>()
            : (await _lots.SearchAsync(new LotSearchRequest(keyword.Trim(), Take: 30), ct)).Items
                .Select(l => new MesCertificateLotDto(l.LotId, l.LotNumber, l.ProductName, l.SerialNumber))
                .ToArray();

        return Ok(new MesCertificateSearchDto(
            documents.Select(d => new MesCertificateDto(
                d.DocumentId, d.LotId, d.LotNumber, d.FileName, d.DocumentVersion, d.CreatedBy, d.CreatedAt)).ToList(),
            lots));
    }

    /// <summary>공정 이력 조회(이력 삭제 화면). LOT 번호로만 찾는다.</summary>
    [HttpGet("process-history")]
    public async Task<ActionResult<IReadOnlyList<ProcessHistoryItemFullDto>>> ProcessHistory(
        [FromQuery] string? lotNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
            return Ok(Array.Empty<ProcessHistoryItemFullDto>());

        return Ok(await _historyQuery.SearchAsync(new ProcessHistorySearchRequest(LotNumber: lotNumber.Trim()), ct));
    }

    /// <summary>
    /// 공정 이력 한 건을 무효화한다. <b>행을 지우지 않고 "무효화됨" 만 표시한다</b> —
    /// 무엇이 있었는지 지워 버리면 나중에 왜 그렇게 됐는지 알 수 없다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("process-history/{processHistoryId:int}/void")]
    public async Task<ActionResult<MesVoidResultDto>> Void(
        int processHistoryId, [FromBody] MesVoidRequest request, CancellationToken ct)
    {
        try
        {
            await _void.VoidAsync(processHistoryId, request.Reason?.Trim() ?? string.Empty, ct);
            return Ok(new MesVoidResultDto(true, "이력이 무효화되었습니다."));
        }
        catch (ValidationException ex)
        {
            return Ok(new MesVoidResultDto(false, string.Join(" / ", ex.Errors)));
        }
        catch (UnauthorizedException ex)
        {
            return Ok(new MesVoidResultDto(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new MesVoidResultDto(false, ex.Message));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES 이력 무효화 실패 ({ProcessHistoryId})", processHistoryId);
            return Ok(new MesVoidResultDto(false, "무효화 중 문제가 발생했습니다. 관리자에게 문의하세요."));
        }
    }
}

public record MesCertificateDto(
    int DocumentId, int LotId, string LotNumber, string FileName,
    int DocumentVersion, string CreatedBy, DateTime CreatedAt);

/// <summary>새 버전을 올릴 대상으로 고를 수 있는 LOT.</summary>
public record MesCertificateLotDto(int LotId, string LotNumber, string ProductName, string SerialNumber);

public record MesCertificateSearchDto(
    IReadOnlyList<MesCertificateDto> Documents,
    IReadOnlyList<MesCertificateLotDto> Lots);

public record MesVoidRequest(string? Reason);

public record MesVoidResultDto(bool Success, string Message);
