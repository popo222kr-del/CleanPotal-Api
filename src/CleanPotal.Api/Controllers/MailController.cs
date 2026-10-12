using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// OFFICE › 메일 확인 — 웹메일 용량이 모자라 못 보는 메일을 백업 ZIP 으로 올려 두고 찾아본다.
///  - 본인 메일만: 모든 조회·지우기가 로그인한 사람 번호로 걸러진다(관리자도 남의 메일은 못 본다).
///  - 사내 전용: 외부 접속 제한 설정과 상관없이 사내 IP 대역에서만(MailGuardFilter).
///  - 시험 사용: 허용된 계정만(처음에는 1004).
/// </summary>
[ApiController]
[Route("api/mail")]
[Authorize]
[MenuGate("/mail")]
[TypeFilter(typeof(MailGuardFilter))]
public class MailController : ControllerBase
{
    /// <summary>나눠 올리는 한 조각 최대 크기 — IIS 기본 요청 한도(약 28MB)보다 넉넉히 작게.</summary>
    public const int MaxChunkBytes = 8 * 1024 * 1024;
    /// <summary>ZIP 한 개 최대 크기.</summary>
    public const long MaxFileBytes = 4L * 1024 * 1024 * 1024;

    private readonly CleanPotalDbContext _db;
    private readonly MailArchiveStore _store;
    private readonly MailImportJobs _jobs;
    private readonly ExternalAccessPolicy _ext;
    public MailController(CleanPotalDbContext db, MailArchiveStore store, MailImportJobs jobs, ExternalAccessPolicy ext)
    {
        _db = db; _store = store; _jobs = jobs; _ext = ext;
    }

    private int Uid => int.Parse(User.FindFirst("uid")!.Value);
    private MailArchiveService Svc => new(_db, _store);

    public record AccessDto(bool Allowed, bool Internal, string Ip);

    /// <summary>이 사람이 지금 메일 확인을 쓸 수 있는가 — 메뉴 표시·안내용(이 동작만 확인 필터를 거치지 않는다).</summary>
    [HttpGet("access")]
    public async Task<ActionResult<AccessDto>> Access()
    {
        var user = HttpContext.Items["auth_user"] as User ?? await _db.Users.FindAsync(Uid);
        var allowed = user is not null && await MailAccess.AllowedAsync(_db, user);
        var cfg = await _ext.GetAsync();
        var ip = HttpContext.Connection.RemoteIpAddress;
        return Ok(new AccessDto(allowed, ExternalAccessPolicy.IsInternal(ip, cfg.InternalRanges),
            ip is null ? "" : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()));
    }

    public record MailRow(long Id, string Folder, string Subject, string FromName, string FromAddr, string To, string? Date,
        string Preview, int Attachments, long Size);
    public record MailPage(int Total, int Page, int Size, int Inbox, int Sent, List<MailRow> Items);

    /// <summary>목록 — 최근 메일부터. q 는 제목·보낸 사람·받는 사람·본문 앞부분에서 찾는다(띄어 쓴 낱말은 모두 들어간 것).</summary>
    [HttpGet]
    public async Task<ActionResult<MailPage>> List([FromQuery] string? folder, [FromQuery] string? q,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        size = Math.Clamp(size, 10, 200);
        page = Math.Max(1, page);
        var uid = Uid;
        var mine = _db.MailItems.AsNoTracking().Where(m => m.UserId == uid);
        var inbox = await mine.CountAsync(m => m.Folder == "inbox");
        var sent = await mine.CountAsync(m => m.Folder == "sent");

        var query = mine;
        if (folder is "inbox" or "sent") query = query.Where(m => m.Folder == folder);
        foreach (var word in (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
            query = query.Where(m => m.Subject.Contains(word) || m.FromName.Contains(word) || m.FromAddr.Contains(word)
                                     || m.To.Contains(word) || m.Cc.Contains(word) || m.Preview.Contains(word));
        if (DateOnly.TryParse(from, out var f)) { var d = f.ToDateTime(TimeOnly.MinValue); query = query.Where(m => m.Date >= d); }
        if (DateOnly.TryParse(to, out var t)) { var d = t.AddDays(1).ToDateTime(TimeOnly.MinValue); query = query.Where(m => m.Date < d); }

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync();
        return Ok(new MailPage(total, page, size, inbox, sent, rows.Select(m => new MailRow(m.Id, m.Folder, m.Subject, m.FromName, m.FromAddr,
            m.To, m.Date?.ToString("yyyy-MM-dd HH:mm"), m.Preview, m.AttachmentCount, m.Size)).ToList()));
    }

    private Task<MailItem?> MineAsync(long id) { var uid = Uid; return _db.MailItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.UserId == uid); }

    /// <summary>한 통 — 정리한 HTML 본문(또는 글)과 첨부 목록.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<MailDetail>> Get(long id)
    {
        var m = await MineAsync(id);
        if (m is null) return NotFound(new { error = "메일을 찾을 수 없습니다." });
        try { return Ok(Svc.Detail(m)); }
        catch (FileNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>첨부 하나 받기.</summary>
    [HttpGet("{id:long}/attachments/{index:int}")]
    public async Task<IActionResult> Attachment(long id, int index)
    {
        var m = await MineAsync(id);
        if (m is null) return NotFound(new { error = "메일을 찾을 수 없습니다." });
        try
        {
            var a = Svc.Attachment(m, index);
            return a is null ? NotFound(new { error = "첨부를 찾을 수 없습니다." }) : File(a.Value.Bytes, a.Value.ContentType, a.Value.Name);
        }
        catch (FileNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>원본(.eml) 받기 — Outlook 등에서 그대로 열 수 있다.</summary>
    [HttpGet("{id:long}/eml")]
    public async Task<IActionResult> Original(long id)
    {
        var m = await MineAsync(id);
        if (m is null) return NotFound(new { error = "메일을 찾을 수 없습니다." });
        var path = Svc.OriginalPath(m);
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "메일 원본 파일을 찾지 못했습니다." });
        var name = string.Concat((string.IsNullOrWhiteSpace(m.Subject) ? "메일" : m.Subject).Split(Path.GetInvalidFileNameChars()));
        return PhysicalFile(path, "message/rfc822", (name.Length > 80 ? name[..80] : name) + ".eml");
    }

    /// <summary>
    /// ZIP 나눠 올리기 — 본문이 파일 조각 그대로(application/octet-stream). offset 은 지금까지 올린 길이.
    /// 같은 조각을 다시 보내도(재시도) 한 번만 붙는다.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxChunkBytes + 1024)]
    public async Task<ActionResult<long>> Upload([FromQuery] string id, [FromQuery] long offset)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return BadRequest(new { error = "올리기 번호가 잘못됐습니다." });
        Directory.CreateDirectory(_store.UploadDir);
        var path = _store.UploadPath(Uid, id);
        var have = System.IO.File.Exists(path) ? new FileInfo(path).Length : 0;
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        if (ms.Length == 0 || ms.Length > MaxChunkBytes) return BadRequest(new { error = "조각 크기가 잘못됐습니다." });
        if (offset + ms.Length == have) return Ok(have);   // 이미 받은 조각(재시도)
        if (offset != have) return Conflict(new { error = $"올리던 위치가 맞지 않습니다({have}). 처음부터 다시 올려 주세요." });
        if (have + ms.Length > MaxFileBytes) return BadRequest(new { error = "파일이 너무 큽니다(4GB까지)." });
        await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write))
        {
            ms.Position = 0;
            await ms.CopyToAsync(fs);
        }
        return Ok(have + ms.Length);
    }

    public record ImportRequest(string UploadId, string FileName, long Size);
    public record JobDto(string Id, string FileName, string Status, int Processed, MailImportResult? Result, string? Error);
    private static JobDto ToDto(MailImportJobs.Job j) => new(j.Id, j.FileName, j.Status, j.Processed, j.Result, j.Error);

    /// <summary>다 올린 파일 가져오기 시작 — 뒤에서 돌고, 진행은 import/{jobId} 로 묻는다.</summary>
    [HttpPost("import")]
    public ActionResult<JobDto> Import([FromBody] ImportRequest req)
    {
        if (!Guid.TryParseExact(req.UploadId ?? "", "N", out _)) return BadRequest(new { error = "올리기 번호가 잘못됐습니다." });
        var path = _store.UploadPath(Uid, req.UploadId!);
        if (!System.IO.File.Exists(path)) return BadRequest(new { error = "올린 파일이 없습니다. 다시 올려 주세요." });
        if (new FileInfo(path).Length != req.Size) return BadRequest(new { error = "파일이 다 올라가지 않았습니다. 다시 올려 주세요." });
        var name = Path.GetFileName(req.FileName ?? "");
        return Ok(ToDto(_jobs.Start(Uid, path, name.Length > 0 ? name : "메일.zip")));
    }

    [HttpGet("import/{jobId}")]
    public ActionResult<JobDto> ImportStatus(string jobId)
    {
        var j = _jobs.Get(jobId, Uid);
        return j is null ? NotFound(new { error = "가져오기 작업을 찾을 수 없습니다(서버가 다시 시작됐을 수 있습니다)." }) : Ok(ToDto(j));
    }

    public record ImportRow(int Id, string FileName, string ImportedAt, long Bytes, int Total, int Added, int Duplicates, int Failed);

    /// <summary>가져오기 기록(최근 것부터).</summary>
    [HttpGet("imports")]
    public async Task<ActionResult<List<ImportRow>>> Imports()
    {
        var uid = Uid;
        var rows = await _db.MailImports.AsNoTracking().Where(i => i.UserId == uid).OrderByDescending(i => i.Id).Take(200).ToListAsync();
        return Ok(rows.Select(i => new ImportRow(i.Id, i.FileName, i.ImportedAt.ToString("yyyy-MM-dd HH:mm"), i.Bytes,
            i.Total, i.Added, i.Duplicates, i.Failed)).ToList());
    }

    /// <summary>가져오기 한 번 취소 — 그때 들어온 메일을 원본째 지운다(웹메일 서버의 메일은 건드리지 않는다).</summary>
    [HttpDelete("imports/{id:int}")]
    public async Task<ActionResult<int>> DeleteImport(int id)
    {
        var uid = Uid;
        if (!await _db.MailImports.AnyAsync(i => i.Id == id && i.UserId == uid)) return NotFound(new { error = "가져오기 기록을 찾을 수 없습니다." });
        return Ok(await Svc.DeleteImportAsync(uid, id));
    }
}
