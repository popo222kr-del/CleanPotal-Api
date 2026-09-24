using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Screens;
using ProductionManagement.Infrastructure.Imaging;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES LOT 조회 API — MES 화면을 React 로 옮기는 첫 화면("LOT 현황 조회")이 쓴다.
///
/// 조회 로직은 MES 업무 계층(ILotHistoryService)을 그대로 부른다. 같은 규칙을 포털에 다시 구현하면
/// 두 곳이 갈라지기 때문이다.
///
/// 권한: MES 전용 영역(mes). 전 직원이 권한을 받아 쓰는 시스템이라 현장 점검 권한에 얹어 두지 않는다
/// — 얹어 두면 "MES 만 쓰는 사람"·"MES 는 빼는 사람"을 만들 수 없다. 기본 등급은 1(조회)이다.
/// </summary>
[ApiController]
[Route("api/mes/lot")]
[Authorize(Policy = "ViewMes")]
public class MesLotController : ControllerBase
{
    // 휴대폰 사진은 크다. MES 화면과 같은 상한을 쓴다.
    private const long MaxPhotoBytes = 20L * 1024 * 1024;

    private readonly ILotHistoryService _history;
    private readonly ILotService _lots;
    private readonly IRegistrationService _registrations;
    private readonly BarcodeService _barcodes;
    private readonly ILogger<MesLotController> _log;
    public MesLotController(
        ILotHistoryService history,
        ILotService lots,
        IRegistrationService registrations,
        BarcodeService barcodes,
        ILogger<MesLotController> log)
    {
        _history = history;
        _lots = lots;
        _registrations = registrations;
        _barcodes = barcodes;
        _log = log;
    }

    /// <summary>
    /// LOT번호 · S/N · 반출번호 중 아무거나 한 칸에 넣고 찾는다(MES 화면의 통합 검색과 같은 규칙).
    /// 찾지 못하면 null 을 돌려준다 — 404 가 아니다. "없음"은 오류가 아니라 정상적인 조회 결과다.
    /// </summary>
    /// <remarks>
    /// 화면이 한 번에 네 덩어리(헤더·TRAN 이력·입출고 사이클·검사 파라미터)를 모두 그리므로
    /// 왕복을 네 번 하지 않고 한 번에 내려준다.
    /// </remarks>
    [HttpGet("history")]
    public async Task<ActionResult<MesLotHistoryDto?>> History([FromQuery] string? keyword, CancellationToken ct)
    {
        var lotId = await _history.FindLotIdByKeywordAsync(keyword, ct);
        if (lotId is null) return Ok(null);

        var header = await _history.GetHeaderAsync(lotId.Value, ct);
        // 아래 셋은 LOT 하나가 아니라 같은 S/N(=같은 물리 부품)의 이력 전체를 모은다.
        // 같은 부품이 여러 번 입고·출고된 내력을 한 화면에서 보려는 것이다.
        var transitions = await _history.GetTransitionsBySerialNumberAsync(header.SerialNumber, ct);
        var cycles = await _history.GetCyclesBySerialNumberAsync(header.SerialNumber, ct);
        var parameters = await _history.GetParameterRecordsBySerialNumberAsync(header.SerialNumber, ct);

        return Ok(new MesLotHistoryDto(header, transitions, cycles, parameters));
    }

    /// <summary>
    /// LOT 스캔 — 찍거나 입력한 값으로 LOT 을 찾아 "어느 OPER 화면에서 처리하면 되는지" 알려준다.
    /// 공정을 옮기지는 않는다. 처리(TRAN)는 OPER 화면의 기존 게이트(사유코드·레시피·SPEC OUT 등)를 그대로 거친다.
    /// 못 찾은 것도 정상적인 결과라 200 으로 돌려주고, 화면이 사유를 그대로 보여준다.
    /// </summary>
    [HttpGet("scan")]
    public async Task<ActionResult<MesScanResultDto>> Scan([FromQuery] string? code, CancellationToken ct)
    {
        var text = LotScanCode.Normalize(code);
        if (text is null)
            return Ok(MesScanResultDto.NotFound(code, "스캔한 값이 비어 있습니다."));

        var lotId = await _history.FindLotIdByKeywordAsync(text, ct);
        if (lotId is null)
            return Ok(MesScanResultDto.NotFound(text, $"'{text}' 에 해당하는 LOT 을 찾을 수 없습니다."));

        var header = await _history.GetHeaderAsync(lotId.Value, ct);
        var hasScreen = OperScreens.Has(header.CurrentOperCode);
        return Ok(new MesScanResultDto(
            true, text, header.LotId, header.LotNumber, header.SerialNumber,
            header.CurrentOperCode, header.CurrentOperName, hasScreen,
            hasScreen
                ? null
                : $"LOT {header.LotNumber} 은(는) 현재 {header.CurrentOperCode} {header.CurrentOperName} 단계라 "
                  + "OPER 화면에서 처리할 대상이 아닙니다."));
    }

    /// <summary>
    /// 사진에서 바코드·QR 을 읽는다.
    ///
    /// 왜 서버가 읽는가: 브라우저 실시간 카메라(getUserMedia)는 HTTPS 에서만 허용되는데 사내 Wi-Fi 는
    /// HTTP 로 접속한다. 그래서 휴대폰 카메라로 "사진"을 찍어 올리면 서버가 읽는다.
    /// </summary>
    [HttpPost("decode")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<ActionResult<MesDecodeDto>> Decode(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Ok(new MesDecodeDto(null, "사진이 비어 있습니다."));
        if (file.Length > MaxPhotoBytes)
            return Ok(new MesDecodeDto(null, "사진이 너무 큽니다(최대 20MB)."));

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        // 해독은 CPU 를 꽤 쓴다. 요청 스레드를 붙잡지 않게 넘긴다.
        var text = await Task.Run(() => _barcodes.Decode(buffer.ToArray()), ct);
        return Ok(text is null
            ? new MesDecodeDto(null, "사진에서 바코드·QR 을 읽지 못했습니다. 더 가까이, 흔들리지 않게 다시 찍어 주세요.")
            : new MesDecodeDto(text, null));
    }

    /// <summary>
    /// LOT 라벨·화면 표시용 QR. 이미지 파일이 아니라 base64 로 내려준다 —
    /// &lt;img src&gt; 로는 인증 헤더를 실을 수 없어서, 보통 API 처럼 받아 data URL 로 붙인다.
    /// </summary>
    [HttpGet("qr")]
    public ActionResult<MesQrDto> Qr([FromQuery] string? value)
    {
        var text = LotScanCode.Normalize(value);
        if (text is null) return BadRequest(new { error = "QR 로 만들 값이 없습니다." });
        return Ok(new MesQrDto(Convert.ToBase64String(_barcodes.EncodeQrPng(text))));
    }

    /// <summary>
    /// LOT 정보 수정 창이 열릴 때 필요한 값 — 이 LOT 을 만든 전산등록의 반출번호 · LINE.
    /// 전산등록을 거치지 않고 생긴 LOT 은 없을 수 있다(그러면 반출번호 · LINE 을 고칠 수 없다).
    /// </summary>
    [HttpGet("{lotId:int}/edit")]
    public async Task<ActionResult<MesLotEditDto>> Edit(int lotId, CancellationToken ct)
    {
        var registration = await _registrations.GetByLotIdAsync(lotId, ct);
        return Ok(new MesLotEditDto(
            registration is not null,
            registration?.ExportNumber,
            registration?.Line));
    }

    /// <summary>
    /// LOT 정보 수정. 고칠 수 있는 것만 고친다 —
    /// 반출번호 · LINE 은 전산등록이 있어야 하고, S/N 은 실제로 바뀌었을 때만 건드린다
    /// (같은 값으로 다시 저장하면 이력에 의미 없는 변경이 쌓인다).
    /// PROCESS 는 화면에 없지만 기존 값을 그대로 유지한다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{lotId:int}/edit")]
    public async Task<ActionResult<MesLotEditResultDto>> SaveEdit(
        int lotId, [FromBody] MesLotEditRequest request, CancellationToken ct)
    {
        try
        {
            // 출하 완료 LOT 은 코멘트 저장(마지막 단계)에서야 거절된다. 그 전에 반출번호·LINE·S/N 이 먼저
            // 저장돼 버려, 화면에는 실패로 뜨는데 일부만 바뀌어 있었다. 처음에 한 번 확인하고 아무것도 바꾸지 않는다.
            var detail = await _lots.GetDetailAsync(lotId, ct);
            if (detail is null)
                return Ok(new MesLotEditResultDto(false, "LOT 을 찾을 수 없습니다."));
            if (detail.Header.CurrentStatus == ProductionManagement.Domain.Enums.LotStatus.Completed)
                return Ok(new MesLotEditResultDto(false, "출하 완료된 LOT은 수정할 수 없습니다. (성적서는 변경 가능)"));

            var registration = await _registrations.GetByLotIdAsync(lotId, ct);
            if (registration is not null)
            {
                await _registrations.UpdateAsync(registration.RegistrationId, new RegistrationUpdateRequest(
                    (request.ExportNumber ?? "").Trim(),
                    (request.Line ?? "").Trim(),
                    registration.ProcessLabel ?? ""), ct);
            }

            var serial = request.SerialNumber?.Trim();
            if (!string.IsNullOrWhiteSpace(serial) && serial != request.OriginalSerialNumber?.Trim())
                await _lots.UpdateSerialNumberAsync(lotId, serial, ct);

            await _lots.UpdateCurrentCommentAsync(lotId, request.Comment, ct);
            return Ok(new MesLotEditResultDto(true, "저장되었습니다."));
        }
        catch (ValidationException ex)
        {
            return Ok(new MesLotEditResultDto(false, string.Join(" / ", ex.Errors)));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new MesLotEditResultDto(false, ex.Message));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES LOT 정보 저장 실패 ({LotId})", lotId);
            return Ok(new MesLotEditResultDto(false, "저장 중 문제가 발생했습니다. 관리자에게 문의하세요."));
        }
    }

    /// <summary>
    /// TAT 조회 — 기간 안에 고객출하까지 끝난 LOT 의 입고→출하 소요 시간.
    /// 기간을 안 주면 최근 30일. 끝날이 시작날보다 앞이면 두 값을 바꿔서 본다
    /// (빈 결과를 돌려주고 "왜 안 나오지" 하게 만들 이유가 없다).
    /// </summary>
    [HttpGet("tat")]
    public async Task<ActionResult<TatReportDto>> Tat(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var end = (to ?? DateTime.Today).Date;
        var start = (from ?? end.AddDays(-30)).Date;
        if (start > end) (start, end) = (end, start);
        return Ok(await _lots.GetTatReportAsync(start, end, ct));
    }
}

/// <summary>"LOT 현황 조회" 한 화면이 필요로 하는 전부. 포털 API 전용 묶음이라 API 프로젝트에 둔다.</summary>
/// <summary>LOT 스캔 결과. 주소를 서버가 만들지 않는다 — 화면 경로는 화면이 정한다.</summary>
public record MesScanResultDto(
    bool Found,
    string? ScannedText,
    int LotId,
    string? LotNumber,
    string? SerialNumber,
    int OperCode,
    string? OperName,
    bool HasOperScreen,
    string? Message)
{
    public static MesScanResultDto NotFound(string? text, string message)
        => new(false, text, 0, null, null, 0, null, false, message);
}

/// <summary>사진 해독 결과. 못 읽은 것은 오류가 아니라 결과라 <paramref name="Message"/> 로 사유를 준다.</summary>
public record MesDecodeDto(string? Text, string? Message);

/// <summary><paramref name="HasRegistration"/> 가 false 면 반출번호 · LINE 을 고칠 수 없다.</summary>
public record MesLotEditDto(bool HasRegistration, string? ExportNumber, string? Line);

/// <summary><paramref name="OriginalSerialNumber"/>: 열었을 때의 S/N. 바뀐 경우에만 저장한다.</summary>
public record MesLotEditRequest(
    string? ExportNumber, string? Line, string? SerialNumber, string? OriginalSerialNumber, string? Comment);

public record MesLotEditResultDto(bool Success, string Message);

/// <summary>QR PNG 의 base64.</summary>
public record MesQrDto(string PngBase64);

public record MesLotHistoryDto(
    LotHistoryHeaderDto Header,
    IReadOnlyList<LotTransitionRowDto> Transitions,
    IReadOnlyList<LotCycleRowDto> Cycles,
    IReadOnlyList<LotParameterRecordDto> Parameters);
