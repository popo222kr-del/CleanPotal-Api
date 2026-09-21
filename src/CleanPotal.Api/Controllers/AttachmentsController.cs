using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 첨부 파일 보관소. 파일은 디스크에 두고 DB 에는 어디 있는지만 남긴다.
///
/// 어느 화면이 쓰는지는 여기서 따지지 않는다 — 기록 칸에 적힌 문자열이 곧 참조다.
///
/// 받기는 로그인만 요구한다. 조회 등급만 있는 사람도 자기가 볼 수 있는 기록의 첨부는
/// 봐야 하고, 이 보관소는 화면 여럿이 같이 써서 한 영역으로 묶을 수 없다.
/// 올리기는 EditAttachment — 어디든 무언가를 고칠 수 있는 사람만 디스크에 쓴다.
/// 기록 자체를 저장하는 일은 그 화면의 API 가 따로 가른다.
/// </summary>
[ApiController]
[Route("api/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    // 사진은 화면에서 1400px JPEG 로 줄여 올리므로 이 상한에 걸릴 일이 거의 없다.
    private const long MaxBytes = 30L * 1024 * 1024;
    private const int MaxFilesPerCall = 20;

    private readonly CleanPotalDbContext _db;
    private readonly AttachmentStore _store;
    public AttachmentsController(CleanPotalDbContext db, AttachmentStore store)
    {
        _db = db;
        _store = store;
    }

    [Authorize(Policy = "EditAttachment")]
    [HttpPost]
    [RequestSizeLimit(MaxBytes * MaxFilesPerCall)]
    public async Task<ActionResult<IReadOnlyList<AttachmentDto>>> Upload(CancellationToken ct)
    {
        // 칸 이름을 가리지 않고 넘어온 파일을 모두 받는다 — 부르는 쪽마다 이름이 다를 이유가 없다.
        var files = Request.HasFormContentType ? Request.Form.Files : null;
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "올릴 파일이 없습니다." });
        if (files.Count > MaxFilesPerCall)
            return BadRequest(new { error = $"한 번에 {MaxFilesPerCall}개까지 올릴 수 있습니다." });

        var who = User.Identity?.Name ?? "";
        var result = new List<AttachmentDto>();
        foreach (var f in files)
        {
            if (f.Length <= 0) continue;
            if (f.Length > MaxBytes)
                return BadRequest(new { error = $"'{f.FileName}' 이 너무 큽니다 (한 개 {MaxBytes / 1024 / 1024}MB 까지)." });

            var row = await _store.SaveAsync(f, who, ct);
            _db.Attachments.Add(row);
            await _db.SaveChangesAsync(ct);
            result.Add(ToDto(row));
        }
        if (result.Count == 0) return BadRequest(new { error = "올릴 파일이 없습니다." });
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var row = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return NotFound(new { error = "없는 첨부입니다." });

        var path = _store.PathOf(row);
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "파일이 보관소에 없습니다. 옮기거나 지워졌을 수 있습니다." });

        var type = string.IsNullOrWhiteSpace(row.ContentType) ? "application/octet-stream" : row.ContentType;
        // 사진은 화면에 바로 띄우고, 그 밖의 파일은 받게 한다.
        return PhysicalFile(path, type, row.Kind == "image" ? null : row.FileName);
    }

    internal static AttachmentDto ToDto(Attachment a) =>
        new(a.Id, a.FileName, a.ContentType, a.Size, a.Kind,
            $"att:{a.Id}|{Uri.EscapeDataString(a.FileName)}|{a.Kind}");
}

/// <summary>첨부 파일을 디스크에 넣고 빼는 일만 한다.</summary>
public class AttachmentStore
{
    private readonly string _root;

    public AttachmentStore(IConfiguration cfg, IWebHostEnvironment env)
    {
        // 설정이 없으면 앱 폴더 밑 App_Data/attachments. wwwroot 밑에 두면 안 된다 —
        // 거기 있으면 로그인 없이도 주소만 알면 받아 갈 수 있다.
        var configured = (cfg["Storage:AttachmentsPath"] ?? "").Trim();
        _root = configured.Length > 0
            ? configured
            : Path.Combine(env.ContentRootPath, "App_Data", "attachments");
        Directory.CreateDirectory(_root);
    }

    public string PathOf(Attachment a) => Path.Combine(_root, a.Folder, a.StoredName);

    public async Task<Attachment> SaveAsync(IFormFile f, string who, CancellationToken ct)
    {
        var folder = DateTime.Now.ToString("yyyyMM");
        Directory.CreateDirectory(Path.Combine(_root, folder));

        // 올린 이름을 그대로 파일명으로 쓰면 경로 조작(..\)과 겹침이 생긴다. 확장자만 가져온다.
        var ext = Path.GetExtension(f.FileName);
        if (ext.Length > 16 || ext.Any(c => Path.GetInvalidFileNameChars().Contains(c))) ext = "";
        var stored = $"{Guid.NewGuid():N}{ext}";

        await using (var dst = System.IO.File.Create(Path.Combine(_root, folder, stored)))
            await f.CopyToAsync(dst, ct);

        return new Attachment
        {
            StoredName = stored,
            Folder = folder,
            FileName = Path.GetFileName(f.FileName),
            ContentType = f.ContentType ?? "",
            Size = f.Length,
            Kind = (f.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image" : "file",
            CreatedBy = who,
        };
    }
}
