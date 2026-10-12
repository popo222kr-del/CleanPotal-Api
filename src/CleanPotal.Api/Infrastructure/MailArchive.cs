using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanPotal.Core;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Ganss.Xss;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 메일 확인 — 보관 위치. 원본 메일(.eml)을 사람별 폴더에 SHA-256 이름으로 둔다.
///   {root}/{사용자번호}/{해시 앞 2자}/{해시}.eml
///   {root}/_upload/…part   (나눠 올리는 중인 ZIP — 가져오기가 끝나면 지운다)
/// 위치는 Storage:MailArchivePath, 없으면 앱 폴더 밑 App_Data/mail-archive(배포 때 보존된다).
/// 남의 메일이라 첨부 보관소(NAS 공유 폴더)와 섞지 않고 따로 둔다. wwwroot 밑에 두면 안 된다.
/// </summary>
public sealed class MailArchiveStore
{
    public string Root { get; }

    public MailArchiveStore(IConfiguration cfg, IWebHostEnvironment env)
    {
        var configured = (cfg["Storage:MailArchivePath"] ?? "").Trim();
        Root = configured.Length > 0 ? configured : Path.Combine(env.ContentRootPath, "App_Data", "mail-archive");
    }

    /// <summary>시험용 — 정해진 폴더를 쓴다.</summary>
    public MailArchiveStore(string root) => Root = root;

    public string PathOf(int userId, string hash) => Path.Combine(Root, userId.ToString(), hash[..2], hash + ".eml");

    public string UploadDir => Path.Combine(Root, "_upload");
    public string UploadPath(int userId, string uploadId) => Path.Combine(UploadDir, $"{userId}_{uploadId}.part");

    /// <summary>하루 지난 올리다 만 파일 정리.</summary>
    public void CleanStaleUploads()
    {
        try
        {
            if (!Directory.Exists(UploadDir)) return;
            foreach (var f in Directory.EnumerateFiles(UploadDir, "*.part"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f);
        }
        catch (Exception) { /* 정리는 다음에 */ }
    }
}

/// <summary>
/// 메일 확인을 쓸 수 있는 계정과 사내 접속 확인.
///  - 계정: CheckSettings 'site:mail-users'(아이디 JSON 배열). 없으면 1004 만 — 시험 사용 중.
///  - 사내: 외부 접속 보안의 사내 IP 대역 안에서만. 외부 접속 제한을 켰는지와 상관없이 늘 사내 전용이다
///    (남의 눈에 띄면 안 되는 개인 메일이라 휴대폰 데이터로는 열지 않는다).
/// </summary>
public static class MailAccess
{
    public const string SettingKey = "site:mail-users";
    public static readonly string[] DefaultUsers = { "1004" };

    public static async Task<bool> AllowedAsync(CleanPotalDbContext db, User user)
    {
        var raw = (await db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey))?.Value;
        string[] users = DefaultUsers;
        if (!string.IsNullOrWhiteSpace(raw))
            try { users = JsonSerializer.Deserialize<string[]>(raw) ?? DefaultUsers; } catch (JsonException) { }
        return !user.IsResigned && users.Contains(user.Username, StringComparer.Ordinal);
    }
}

/// <summary>메일 확인 API 앞에서 계정·사내 접속을 확인한다(사용 가능 여부를 묻는 Access 동작만 빼고).</summary>
public sealed class MailGuardFilter : IAsyncActionFilter
{
    private readonly CleanPotalDbContext _db;
    private readonly ExternalAccessPolicy _ext;
    public MailGuardFilter(CleanPotalDbContext db, ExternalAccessPolicy ext) { _db = db; _ext = ext; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.ActionName;
        if (action != "Access")
        {
            var http = context.HttpContext;
            var user = http.Items["auth_user"] as User;
            if (user is null && int.TryParse(http.User.FindFirst("uid")?.Value, out var uid)) user = await _db.Users.FindAsync(uid);
            if (user is null || !await MailAccess.AllowedAsync(_db, user))
                throw new ForbiddenException("메일 확인은 시험 사용 중이라 허용된 계정만 쓸 수 있습니다.");
            var cfg = await _ext.GetAsync();
            if (!ExternalAccessPolicy.IsInternal(http.Connection.RemoteIpAddress, cfg.InternalRanges))
                throw new ForbiddenException("메일 확인은 사내(회사 와이파이·PC)에서만 열 수 있습니다.");
            http.Items["auth_user"] = user;
        }
        await next();
    }
}

/// <summary>가져오기 결과.</summary>
public sealed record MailImportResult(int ImportId, int Total, int Added, int Duplicates, int Failed);

public sealed record MailAttachmentInfo(int Index, string Name, string ContentType, long Size);
public sealed record MailDetail(long Id, string Folder, string Subject, string From, string To, string Cc, string? Date,
    string? Html, string? Text, List<MailAttachmentInfo> Attachments, bool RemoteImagesBlocked);

/// <summary>
/// 메일 확인 — ZIP/EML 가져오기, 본문·첨부 읽기.
/// 웹메일 백업 ZIP(안에 .eml 여러 개, 메일함별 폴더나 ZIP 이 한 겹 더 있을 수 있다)을 그대로 받는다.
/// </summary>
public sealed class MailArchiveService
{
    /// <summary>한 통 최대 크기 — 이보다 크면 읽지 않고 실패로 센다.</summary>
    public const long MaxMailBytes = 60L * 1024 * 1024;
    /// <summary>본문 속 첨부 그림(cid:)을 바로 보여 줄 최대 크기.</summary>
    private const long MaxInlineImage = 2L * 1024 * 1024;

    private readonly CleanPotalDbContext _db;
    private readonly MailArchiveStore _store;
    public MailArchiveService(CleanPotalDbContext db, MailArchiveStore store) { _db = db; _store = store; }

    // ── 가져오기 ──────────────────────────────────────────────

    /// <param name="progress">(읽은 메일 수) — 화면 진행 표시용</param>
    public async Task<MailImportResult> ImportAsync(User user, string filePath, string fileName, Action<int>? progress = null,
        CancellationToken ct = default)
    {
        var known = (await _db.MailItems.AsNoTracking().Where(m => m.UserId == user.Id).Select(m => m.Hash).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var imp = new MailImport
        {
            UserId = user.Id, FileName = Cut(Path.GetFileName(fileName), 260), ImportedAt = DateTime.Now,
            Bytes = new FileInfo(filePath).Length,
        };
        _db.MailImports.Add(imp);
        await _db.SaveChangesAsync(ct);

        var pending = 0;
        async Task Handle(byte[] bytes, string entryPath)
        {
            imp.Total++;
            try
            {
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!known.Add(hash)) { imp.Duplicates++; return; }
                var item = Index(bytes, entryPath, user.Email);
                item.UserId = user.Id; item.ImportId = imp.Id; item.Hash = hash; item.Size = bytes.Length;
                var path = _store.PathOf(user.Id, hash);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) await File.WriteAllBytesAsync(path, bytes, ct);
                _db.MailItems.Add(item);
                imp.Added++;
                if (++pending >= 200) { await _db.SaveChangesAsync(ct); pending = 0; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                imp.Failed++;
            }
            progress?.Invoke(imp.Total);
        }

        try
        {
            if (IsZip(filePath))
            {
                using var zip = ZipFile.OpenRead(filePath);
                await WalkZipAsync(zip, "", 0, Handle, ct);
                if (imp.Total == 0) throw new InvalidDataException("ZIP 안에 메일(.eml)이 없습니다. 웹메일에서 받은 백업 ZIP 이 맞는지 확인하세요.");
            }
            else
            {
                var len = new FileInfo(filePath).Length;
                if (len > MaxMailBytes) throw new InvalidDataException("메일 파일이 너무 큽니다.");
                await Handle(await File.ReadAllBytesAsync(filePath, ct), fileName);
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // 아무것도 못 넣었으면 기록도 남기지 않는다
            if (imp.Added == 0) { _db.ChangeTracker.Clear(); _db.MailImports.Remove(new MailImport { Id = imp.Id }); await _db.SaveChangesAsync(CancellationToken.None); }
            throw;
        }
        return new MailImportResult(imp.Id, imp.Total, imp.Added, imp.Duplicates, imp.Failed);
    }

    private static bool IsZip(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        return fs.Read(head) == 4 && head[0] == 'P' && head[1] == 'K';
    }

    /// <summary>ZIP 안의 .eml 을 차례로 — 안에 ZIP 이 또 있으면(메일함별 묶음) 두 겹까지 연다.</summary>
    private static async Task WalkZipAsync(ZipArchive zip, string prefix, int depth, Func<byte[], string, Task> handle, CancellationToken ct)
    {
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.FullName.EndsWith('/')) continue;
            var path = prefix + e.FullName;
            if (e.FullName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            {
                if (e.Length > MaxMailBytes) { await handle(Array.Empty<byte>(), path); continue; }   // 실패로 센다
                using var s = e.Open();
                using var ms = new MemoryStream((int)Math.Max(e.Length, 0));
                await s.CopyToAsync(ms, ct);
                await handle(ms.ToArray(), path);
            }
            else if (depth < 2 && e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tmp = Path.GetTempFileName();
                try
                {
                    using (var s = e.Open())
                    await using (var fs = File.Create(tmp)) await s.CopyToAsync(fs, ct);
                    using var inner = ZipFile.OpenRead(tmp);
                    await WalkZipAsync(inner, path + "/", depth + 1, handle, ct);
                }
                finally { try { File.Delete(tmp); } catch (IOException) { } }
            }
        }
    }

    /// <summary>원본에서 목록·검색용 머리 정보를 뽑는다.</summary>
    public static MailItem Index(byte[] bytes, string entryPath, string? ownEmail)
    {
        if (bytes.Length == 0) throw new InvalidDataException("빈 메일");
        using var ms = new MemoryStream(bytes);
        var msg = MimeMessage.Load(ms);
        var from = msg.From.Mailboxes.FirstOrDefault();
        var text = msg.TextBody;
        if (string.IsNullOrWhiteSpace(text) && msg.HtmlBody is { } html) text = HtmlToText(html);
        return new MailItem
        {
            Folder = FolderOf(entryPath, from?.Address, ownEmail),
            Subject = Cut(msg.Subject ?? "", 500),
            FromName = Cut(from?.Name ?? "", 200),
            FromAddr = Cut(from?.Address ?? "", 200),
            To = Cut(Join(msg.To), 1000),
            Cc = Cut(Join(msg.Cc), 1000),
            Date = msg.Date == DateTimeOffset.MinValue ? null : msg.Date.LocalDateTime,
            Preview = Cut(Regex.Replace(text ?? "", @"\s+", " ").Trim(), 300),
            AttachmentCount = msg.Attachments.Count(),
        };
    }

    /// <summary>받은/보낸 — ZIP 안 폴더 이름이 먼저, 없으면 보낸 사람이 본인인지로.</summary>
    public static string FolderOf(string entryPath, string? fromAddr, string? ownEmail)
    {
        var p = entryPath.ToLowerInvariant();
        if (p.Contains("보낸") || p.Contains("sent") || p.Contains("outbox")) return "sent";
        if (p.Contains("받은") || p.Contains("inbox")) return "inbox";
        if (!string.IsNullOrWhiteSpace(ownEmail) && !string.IsNullOrWhiteSpace(fromAddr)
            && string.Equals(fromAddr.Trim(), ownEmail.Trim(), StringComparison.OrdinalIgnoreCase)) return "sent";
        return "inbox";
    }

    private static string Join(InternetAddressList list)
        => string.Join(", ", list.Mailboxes.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : $"{m.Name} <{m.Address}>"));

    private static string HtmlToText(string html)
    {
        var s = Regex.Replace(html, @"<(style|script|head)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(s);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    // ── 읽기 ─────────────────────────────────────────────────

    public MimeMessage Load(MailItem item)
    {
        var path = _store.PathOf(item.UserId, item.Hash);
        if (!File.Exists(path)) throw new FileNotFoundException("메일 원본 파일을 찾지 못했습니다. 보관 폴더를 확인하세요.");
        return MimeMessage.Load(path);
    }

    public string OriginalPath(MailItem item) => _store.PathOf(item.UserId, item.Hash);

    /// <summary>본문(안전하게 정리한 HTML 또는 글)과 첨부 목록.</summary>
    public MailDetail Detail(MailItem item)
    {
        var msg = Load(item);
        string? html = msg.HtmlBody;
        var blocked = false;
        if (html is not null)
        {
            html = InlineCidImages(msg, html);
            // 바깥 그림(열람 추적용이 많다)은 불러오지 않는다 — 깨진 그림 자리도 남기지 않게 태그째 뺀다
            var stripped = RemoteImg.Replace(html, "");
            blocked = stripped.Length != html.Length;
            html = Sanitizer.Sanitize(stripped);
        }
        var atts = msg.Attachments.Select((a, i) => new MailAttachmentInfo(i, AttachmentName(a, i),
            a.ContentType.MimeType, AttachmentSize(a))).ToList();
        return new MailDetail(item.Id, item.Folder, item.Subject,
            string.IsNullOrWhiteSpace(item.FromName) ? item.FromAddr : $"{item.FromName} <{item.FromAddr}>",
            item.To, item.Cc, item.Date?.ToString("yyyy-MM-dd HH:mm"), html, html is null ? msg.TextBody : null, atts, blocked);
    }

    /// <summary>첨부 하나(이름·형식·내용).</summary>
    public (string Name, string ContentType, byte[] Bytes)? Attachment(MailItem item, int index)
    {
        var msg = Load(item);
        var a = msg.Attachments.ElementAtOrDefault(index);
        if (a is null) return null;
        using var ms = new MemoryStream();
        if (a is MimePart part) part.Content?.DecodeTo(ms);
        else if (a is MessagePart mp) mp.Message?.WriteTo(ms);
        return (AttachmentName(a, index), a is MessagePart ? "message/rfc822" : a.ContentType.MimeType, ms.ToArray());
    }

    private static string AttachmentName(MimeEntity a, int i)
    {
        var name = (a as MimePart)?.FileName ?? a.ContentType.Name;
        if (string.IsNullOrWhiteSpace(name) && a is MessagePart mp) name = (mp.Message?.Subject ?? "첨부 메일") + ".eml";
        return string.IsNullOrWhiteSpace(name) ? $"첨부{i + 1}" : name;
    }

    private static long AttachmentSize(MimeEntity a)
    {
        try
        {
            using var counter = new MimeKit.IO.MeasuringStream();
            if (a is MimePart part) part.Content?.DecodeTo(counter);
            else if (a is MessagePart mp) mp.Message?.WriteTo(counter);
            return counter.Length;
        }
        catch (Exception) { return 0; }
    }

    private static readonly Regex RemoteImg = new(@"<img\b[^>]*\bsrc\s*=\s*[""']?\s*(https?:)?//[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>본문 속 cid: 그림(2MB 이하)을 바로 보이게 data: 로 바꾼다.</summary>
    private static string InlineCidImages(MimeMessage msg, string html)
    {
        foreach (var part in msg.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId) || !part.ContentType.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
            using var ms = new MemoryStream();
            part.Content?.DecodeTo(ms);
            if (ms.Length == 0 || ms.Length > MaxInlineImage) continue;
            var data = $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(ms.ToArray())}";
            html = Regex.Replace(html, "cid:" + Regex.Escape(part.ContentId.Trim('<', '>')), data, RegexOptions.IgnoreCase);
        }
        return html;
    }

    /// <summary>메일 HTML 정리 — 스크립트·폼·이벤트 속성을 없앤다. 메일 서식(style)은 살린다.
    /// 바깥 그림(http)은 화면의 격리 칸이 막는다(CSP img-src data:).</summary>
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Add("data");
        s.AllowedSchemes.Add("mailto");
        s.AllowedTags.Add("style");
        s.AllowedTags.Add("font");
        s.AllowedAttributes.Add("bgcolor");
        s.AllowedAttributes.Add("color");
        s.AllowedAttributes.Add("face");
        s.AllowDataAttributes = false;
        return s;
    }

    // ── 지우기 ───────────────────────────────────────────────

    /// <summary>가져오기 한 번에 들어온 메일을 모두 지운다(원본 파일 포함).</summary>
    public async Task<int> DeleteImportAsync(int userId, int importId)
    {
        var items = await _db.MailItems.Where(m => m.UserId == userId && m.ImportId == importId).ToListAsync();
        foreach (var m in items)
            try { File.Delete(_store.PathOf(userId, m.Hash)); } catch (IOException) { }
        _db.MailItems.RemoveRange(items);
        var imp = await _db.MailImports.FirstOrDefaultAsync(i => i.Id == importId && i.UserId == userId);
        if (imp is not null) _db.MailImports.Remove(imp);
        await _db.SaveChangesAsync();
        return items.Count;
    }
}

/// <summary>
/// 가져오기 작업 — ZIP 이 크면 몇 분 걸려 요청 하나로 기다리면 끊긴다(IIS 요청 시간 제한).
/// 뒤에서 돌리고 화면은 진행 상황을 물어본다. 상태는 메모리에만(서버를 다시 켜면 사라진다 — 들어간 메일은 남는다).
/// </summary>
public sealed class MailImportJobs
{
    public sealed class Job
    {
        public string Id { get; init; } = "";
        public int UserId { get; init; }
        public string FileName { get; init; } = "";
        public volatile string Status = "running";   // running · done · failed
        public int Processed;
        public MailImportResult? Result;
        public string? Error;
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly MailArchiveStore _store;
    public MailImportJobs(IServiceScopeFactory scopes, MailArchiveStore store) { _scopes = scopes; _store = store; }

    public Job? Get(string id, int userId) => _jobs.TryGetValue(id, out var j) && j.UserId == userId ? j : null;

    public Job Start(int userId, string tempPath, string fileName)
    {
        foreach (var old in _jobs.Values.Where(j => j.Status != "running" && j.StartedAt < DateTime.UtcNow.AddHours(-2)).ToList())
            _jobs.TryRemove(old.Id, out _);
        var job = new Job { Id = Guid.NewGuid().ToString("N"), UserId = userId, FileName = fileName };
        _jobs[job.Id] = job;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
                var user = await db.Users.FindAsync(userId) ?? throw new InvalidOperationException("사용자를 찾지 못했습니다.");
                var svc = new MailArchiveService(db, _store);
                job.Result = await svc.ImportAsync(user, tempPath, fileName, n => Interlocked.Exchange(ref job.Processed, n));
                job.Status = "done";
            }
            catch (Exception ex)
            {
                job.Error = ex is InvalidDataException ? ex.Message : $"가져오지 못했습니다: {ex.Message}";
                job.Status = "failed";
                Console.WriteLine($"[mail] 가져오기 실패({fileName}): {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempPath); } catch (IOException) { }
                _store.CleanStaleUploads();
            }
        });
        return job;
    }
}
