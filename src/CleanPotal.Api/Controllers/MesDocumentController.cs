using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES 출력 관리 — LOT 의 성적서(문서)와 런시트.
/// 입고검사·출고검사를 완료·출하로 넘길 때 먼저 열리는 창이 쓴다.
/// </summary>
[ApiController]
[Route("api/mes/lot/{lotId:int}")]
[Authorize(Policy = "ViewMes")]
public class MesDocumentController : ControllerBase
{
    private const long MaxUploadBytes = 30L * 1024 * 1024;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDocumentService _documents;
    private readonly ICertificateFillService _certificates;
    private readonly ILotHistoryService _history;
    private readonly IRunsheetGenerator _runsheets;
    private readonly ILogger<MesDocumentController> _log;

    public MesDocumentController(
        IDocumentService documents,
        ICertificateFillService certificates,
        ILotHistoryService history,
        IRunsheetGenerator runsheets,
        ILogger<MesDocumentController> log)
    {
        _documents = documents;
        _certificates = certificates;
        _history = history;
        _runsheets = runsheets;
        _log = log;
    }

    /// <summary>이 LOT 의 성적서 목록. 최신 버전이 맨 앞이다.</summary>
    [HttpGet("documents")]
    public async Task<ActionResult<IReadOnlyList<MesDocumentDto>>> Documents(int lotId, CancellationToken ct)
    {
        var items = await _documents.GetByLotIdAsync(lotId, ct);
        return Ok(items
            .OrderByDescending(d => d.DocumentVersion)
            .Select(d => new MesDocumentDto(d.DocumentId, d.FileName, d.DocumentVersion, d.CreatedBy, d.CreatedAt))
            .ToList());
    }

    /// <summary>최신 성적서를 내려준다(출력 관리 창이 쓴다 — 거기서는 버전을 고르지 않는다).</summary>
    [HttpGet("documents/latest")]
    public async Task<IActionResult> LatestDocument(int lotId, CancellationToken ct)
    {
        var latest = (await _documents.GetByLotIdAsync(lotId, ct))
            .OrderByDescending(d => d.DocumentVersion)
            .FirstOrDefault();
        if (latest is null) return NotFound(new { error = "이 LOT 의 성적서가 아직 없습니다." });

        return SendDocument(latest);
    }

    /// <summary>
    /// 파일 실체는 공유폴더에 있고 DB 에는 경로만 있다. 폴더가 옮겨졌거나 파일이 지워졌을 수 있어
    /// 그때는 404 로 사유를 알린다 — 빈 파일을 받아 여는 것보다 낫다.
    /// </summary>
    private IActionResult SendDocument(DocumentDto document)
    {
        var path = _documents.GetFullPath(document);
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "성적서 파일을 찾을 수 없습니다. 공유폴더를 확인하세요." });

        var name = $"{document.LotNumber}_성적서_v{document.DocumentVersion}{Path.GetExtension(document.FileName)}";
        return PhysicalFile(path, ContentType(document.FileName), name);
    }

    /// <summary>
    /// 고른 버전의 성적서를 내려준다. 목록이 버전별로 보이는데 받기가 늘 최신이면
    /// 어느 줄을 눌렀는지가 아무 뜻이 없다.
    /// </summary>
    [HttpGet("documents/{documentId:int}")]
    public async Task<IActionResult> Document(int lotId, int documentId, CancellationToken ct)
    {
        var document = (await _documents.GetByLotIdAsync(lotId, ct))
            .FirstOrDefault(d => d.DocumentId == documentId);
        if (document is null) return NotFound(new { error = "그 성적서를 찾을 수 없습니다." });

        return SendDocument(document);
    }

    /// <summary>런시트를 만들어 내려준다. 임시 파일은 읽어서 넘긴 뒤 지운다.</summary>
    [HttpGet("runsheet")]
    public async Task<IActionResult> Runsheet(int lotId, CancellationToken ct)
    {
        var header = await _history.GetHeaderAsync(lotId, ct);
        var transitions = string.IsNullOrWhiteSpace(header.SerialNumber)
            ? Array.Empty<LotTransitionRowDto>()
            : (await _history.GetTransitionsBySerialNumberAsync(header.SerialNumber, ct)).ToArray();

        var data = new RunsheetData(
            header.LotNumber, header.SerialNumber, header.MatId, header.MatDesc,
            header.CustomerName, header.Line, DateTime.Now,
            transitions.Select(t => new RunsheetRow(
                t.OperCode, t.OperDesc, t.TranCode, t.TranTime, t.ResId, t.Comment, t.UserDesc)).ToList());

        var path = _runsheets.Generate(data);
        try
        {
            // 파일을 통째로 읽어 넘긴다. 스트림을 열어 둔 채 돌려주면 지울 수 없다.
            var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
            return File(bytes, XlsxContentType, Path.GetFileName(path));
        }
        finally
        {
            try { System.IO.File.Delete(path); } catch (IOException) { /* 임시 파일은 남아도 된다 */ }
        }
    }

    /// <summary>성적서를 새 버전으로 올린다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("documents")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<MesUploadResultDto>> Upload(int lotId, IFormFile? file, CancellationToken ct)
        => await WithTempFileAsync(file, async path =>
        {
            var result = await _documents.UploadAsync(new DocumentUploadRequest(lotId, path), ct);
            return new MesUploadResultDto(true, $"성적서가 v{result.DocumentVersion} 으로 등록되었습니다.");
        }, "성적서 업로드");

    /// <summary>
    /// 특이사항 이미지를 성적서에 넣는다.
    /// 성적서 채우기는 Excel COM 이라 서버에서는 돌지 않는다 — 실패로 답하고 왜인지 알려 준다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("abnormal-image")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<MesUploadResultDto>> AbnormalImage(int lotId, IFormFile? file, CancellationToken ct)
        => await WithTempFileAsync(file, async path =>
        {
            var inserted = await _certificates.InsertAbnormalImageAsync(lotId, path, ct);
            return inserted
                ? new MesUploadResultDto(true, "특이사항 이미지를 성적서에 삽입했습니다.")
                : new MesUploadResultDto(false,
                    "특이사항 이미지를 성적서에 넣지 못했습니다. 웹에서는 성적서 엑셀에 값을 써 넣지 못합니다 "
                    + "— 데스크톱 프로그램에서 넣어 주세요. (이 LOT 에 성적서가 아직 없을 수도 있습니다.)");
        }, "특이사항 업로드");

    /// <summary>올라온 파일을 임시 폴더에 풀어 주고, 끝나면 반드시 지운다.</summary>
    private async Task<ActionResult<MesUploadResultDto>> WithTempFileAsync(
        IFormFile? file, Func<string, Task<MesUploadResultDto>> action, string operation)
    {
        if (file is null || file.Length == 0)
            return Ok(new MesUploadResultDto(false, "파일이 비어 있습니다."));

        var directory = Path.Combine(Path.GetTempPath(), $"mes-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(file.FileName));
        try
        {
            await using (var destination = System.IO.File.Create(path))
                await file.CopyToAsync(destination);

            return Ok(await action(path));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES {Operation} 실패", operation);
            return Ok(new MesUploadResultDto(false, $"{operation} 중 문제가 발생했습니다."));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* 임시 폴더는 남아도 된다 */ }
        }
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => XlsxContentType,
        ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        ".xls" => "application/vnd.ms-excel",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}

public record MesDocumentDto(int DocumentId, string FileName, int DocumentVersion, string CreatedBy, DateTime CreatedAt);

/// <summary>올리기 결과. 실패도 정상적인 답이라 200 으로 내려간다.</summary>
public record MesUploadResultDto(bool Success, string Message);
