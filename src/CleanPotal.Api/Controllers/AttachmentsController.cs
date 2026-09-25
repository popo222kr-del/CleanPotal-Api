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
/// 예외: 현장 점검(field) 영역은 조회(1) 등급도 올린다. 체크시트 NG·작업 전후 사진을
/// 생산직(조회 등급)이 QR 점검 중에 찍기 때문이다.
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

    /// <summary>
    /// 화면에 바로 띄워도 되는 그림 형식. SVG 는 스크립트를 품을 수 있어 여기서 뺀다 — 그대로 내려주면
    /// "새 탭에서 이미지 열기" 한 번에 포털 origin 에서 스크립트가 돌아 로그인 토큰을 가져갈 수 있다.
    /// </summary>
    internal static readonly HashSet<string> InlineImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp",
        // 옛 브라우저·일부 프로그램이 붙이는 비표준 이름. 내보낼 때는 표준 이름으로 바꾼다.
        "image/jpg", "image/pjpeg",
    };

    /// <summary>첨부를 달 수 있는 영역. 권한 영역 이름(DbPermissionHandler)과 같다.</summary>
    internal static readonly HashSet<string> Scopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "schedule", "roster", "handover", "field", "office", "reports", "vendors", "mes",
    };

    private readonly CleanPotalDbContext _db;
    private readonly AttachmentStore _store;
    private readonly IAuthorizationService _auth;
    public AttachmentsController(CleanPotalDbContext db, AttachmentStore store, IAuthorizationService auth)
    {
        _db = db;
        _store = store;
        _auth = auth;
    }

    private async Task<bool> CanViewAsync(string scope)
        => (await _auth.AuthorizeAsync(User, null, new CleanPotal.Api.Infrastructure.DbPermissionRequirement(scope, 1))).Succeeded;

    [HttpPost]
    [RequestSizeLimit(MaxBytes * MaxFilesPerCall)]
    public async Task<ActionResult<IReadOnlyList<AttachmentDto>>> Upload([FromQuery] string? scope, CancellationToken ct)
    {
        // 어느 화면의 첨부인지 적어 두면, 받을 때 그 화면을 볼 수 있는 사람에게만 내준다.
        // 예전에는 번호만 알면 로그인한 누구나 모든 영역의 첨부를 받을 수 있었다.
        scope = (scope ?? "").Trim().ToLowerInvariant();
        if (scope.Length > 0 && !Scopes.Contains(scope))
            return BadRequest(new { error = $"알 수 없는 첨부 영역입니다: {scope}" });
        if (scope.Length > 0 && !await CanViewAsync(scope))
            return Forbid();
        // field 는 조회 등급이면 된다(바로 위에서 확인). 나머지는 어디든 편집 등급이어야 올린다.
        if (scope != "field" && !(await _auth.AuthorizeAsync(User, "EditAttachment")).Succeeded)
            return Forbid();

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
            row.Scope = scope;
            _db.Attachments.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch
            {
                // 기록을 못 남겼으면 디스크의 파일도 지운다 — 어디서도 가리키지 않는 파일이 쌓이지 않게.
                _db.Attachments.Remove(row);
                _store.TryDelete(row);
                throw;
            }
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
        if (row.Scope.Length > 0 && !await CanViewAsync(row.Scope))
            return StatusCode(403, new { error = "이 첨부가 달린 화면을 볼 권한이 없습니다." });

        var path = _store.PathOf(row);
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "파일이 보관소에 없습니다. 옮기거나 지워졌을 수 있습니다." });

        // 브라우저가 내용을 보고 형식을 멋대로 짐작(스니핑)하지 못하게 한다.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var type = string.IsNullOrWhiteSpace(row.ContentType) ? "application/octet-stream" : row.ContentType;
        // 안전한 사진만 화면에 바로 띄운다. 그 밖의 파일은 받게 하고, 브라우저가 실행할 수 있는
        // 형식(SVG·HTML·XML·스크립트)은 올린 사람이 적어 보낸 형식을 버리고 그냥 이진 파일로 내려준다.
        if (InlineImageTypes.Contains(type))
            return PhysicalFile(path, type is "image/jpg" or "image/pjpeg" ? "image/jpeg" : type);
        return PhysicalFile(path, IsActiveContent(type) ? "application/octet-stream" : type, row.FileName);
    }

    private static bool IsActiveContent(string type)
    {
        var t = type.ToLowerInvariant();
        return t.StartsWith("image/") || t.Contains("html") || t.Contains("xml")
            || t.Contains("javascript") || t.Contains("ecmascript");
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

    public void TryDelete(Attachment a)
    {
        try { System.IO.File.Delete(PathOf(a)); } catch { /* 이미 없거나 잠겨 있으면 그대로 둔다 */ }
    }

    public async Task<Attachment> SaveAsync(IFormFile f, string who, CancellationToken ct)
    {
        var folder = DateTime.Now.ToString("yyyyMM");
        Directory.CreateDirectory(Path.Combine(_root, folder));

        // 올린 이름을 그대로 파일명으로 쓰면 경로 조작(..\)과 겹침이 생긴다. 확장자만 가져온다.
        var ext = Path.GetExtension(f.FileName);
        if (ext.Length > 16 || ext.Any(c => Path.GetInvalidFileNameChars().Contains(c))) ext = "";
        var stored = $"{Guid.NewGuid():N}{ext}";

        var path = Path.Combine(_root, folder, stored);
        try
        {
            await using var dst = System.IO.File.Create(path);
            await f.CopyToAsync(dst, ct);
        }
        catch
        {
            // 쓰다 만 파일(연결 끊김·취소)을 남기지 않는다.
            try { System.IO.File.Delete(path); } catch { /* 지우기도 실패하면 어쩔 수 없다 */ }
            throw;
        }

        return new Attachment
        {
            StoredName = stored,
            Folder = folder,
            FileName = Path.GetFileName(f.FileName),
            ContentType = f.ContentType ?? "",
            Size = f.Length,
            Kind = AttachmentsController.InlineImageTypes.Contains(f.ContentType ?? "") ? "image" : "file",
            CreatedBy = who,
        };
    }
}
