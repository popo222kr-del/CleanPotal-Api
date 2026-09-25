using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanPotal.Api.Controllers;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 첨부 보관소 정리 — 두 가지를 한다.
/// 1. DB 기록 칸 안에 통째로 들어 있는 사진·파일(data:...;base64,...)을 보관소 파일로 꺼내고,
///    칸에는 "att:번호|이름|종류" 한 줄만 남긴다.
/// 2. 예전 이름("yyyyMM\GUID.jpg")으로 둔 첨부를 새 규칙("분류\yyyy-MM\날짜_시각_이름")으로 옮긴다 —
///    NAS 를 탐색기로 열어도 어느 화면의 무엇인지 알 수 있게.
///
/// base64 로 1.33배, nvarchar 가 글자당 2바이트라 다시 2배 — 1MB 사진이 DB 에서 2.7MB 를 먹는다.
/// 기타세정 현황·생산팀 요청사항은 최근까지 이렇게 저장했고, 주간보고·BROKEN 의 옛 기록에도 남아 있을 수 있다.
///
/// 운영 자료를 바꾸므로 자동으로 돌지 않는다. 서버에서 명령으로 한 번 실행한다.
///   dotnet CleanPotal.Api.dll migrate-attachments --dry-run   몇 개·몇 MB 인지 보기만
///   dotnet CleanPotal.Api.dll migrate-attachments             실제로 옮기기
///
/// 사이트가 켜져 있어도 된다. 칸을 바꿀 때 "읽은 뒤로 아무도 안 고쳤을 때만" 바꾸고, 그 사이 누가 고쳤으면
/// 그 기록은 건너뛴다(만든 파일도 지운다). 여러 번 실행해도 된다 — 남은 것만 옮긴다.
/// </summary>
public static class InlineImageMigrator
{
    private sealed record Target(Type Entity, string Column, string Scope, string Category, string Label, string? DateColumn);

    private static readonly Target[] Targets =
    [
        new(typeof(Handover), nameof(Handover.Images), "handover", "기타세정", "기타세정", nameof(Handover.CreateDate)),
        new(typeof(ProdReq), nameof(ProdReq.RequestImages), "handover", "생산팀요청", "요청", nameof(ProdReq.CreatedAt)),
        new(typeof(ProdReq), nameof(ProdReq.ActionImages), "handover", "생산팀요청", "조치", nameof(ProdReq.CreatedAt)),
        new(typeof(Report), nameof(Report.MemoAttachments), "reports", "주간보고", "메모", nameof(Report.CreatedAt)),
        new(typeof(Report), nameof(Report.MainAttachments), "reports", "주간보고", "본문", nameof(Report.CreatedAt)),
        new(typeof(ReportBlock), nameof(ReportBlock.FollowUpAttachments), "reports", "주간보고", "후속조치", null),
        new(typeof(BrokenRecord), nameof(BrokenRecord.IncidentReports), "office", "BROKEN", "사고보고", nameof(BrokenRecord.CreatedAt)),
        new(typeof(BrokenRecord), nameof(BrokenRecord.CountermeasureReports), "office", "BROKEN", "대책", nameof(BrokenRecord.CreatedAt)),
        new(typeof(BrokenRecord), nameof(BrokenRecord.TrainingDocs), "office", "BROKEN", "교육자료", nameof(BrokenRecord.CreatedAt)),
        new(typeof(BrokenRecord), nameof(BrokenRecord.TrainingImages), "office", "BROKEN", "교육사진", nameof(BrokenRecord.CreatedAt)),
        new(typeof(BrokenTraining), nameof(BrokenTraining.Documents), "office", "BROKEN", "교육자료", null),
        new(typeof(BrokenTraining), nameof(BrokenTraining.Images), "office", "BROKEN", "교육사진", null),
    ];

    /// <summary>JSON 문자열 하나로 들어 있는 data URL. 칸 모양(배열·객체)과 상관없이 찾는다.</summary>
    private static readonly Regex DataString = new("\"data:(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Result(int Records, int Files, long Bytes, int Skipped, int Failed, int Renamed = 0, int Missing = 0);

    public static async Task<Result> RunAsync(CleanPotalDbContext db, AttachmentStore store, bool dryRun, CancellationToken ct = default)
    {
        Console.WriteLine(dryRun
            ? "[migrate] 미리보기 — 아무것도 바꾸지 않는다."
            : $"[migrate] DB 안의 사진·파일을 첨부 보관소로 옮긴다: {store.Root}");
        if (!dryRun) store.LogHealth();

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        int records = 0, files = 0, skipped = 0, failed = 0;
        long bytes = 0;
        foreach (var t in Targets)
        {
            var et = db.Model.FindEntityType(t.Entity);
            if (et is null) continue;
            var table = Q(et.GetTableName()!);
            var col = Q(et.FindProperty(t.Column)!.GetColumnName());
            var idCol = Q(et.FindProperty("Id")!.GetColumnName());
            var dateCol = t.DateColumn is null ? null : Q(et.FindProperty(t.DateColumn)!.GetColumnName());

            var ids = new List<int>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT {idCol} FROM {table} WHERE {col} LIKE '%\"data:%'";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) ids.Add(Convert.ToInt32(r.GetValue(0)));
            }
            if (ids.Count == 0) continue;

            int tRecords = 0, tFiles = 0;
            long tBytes = 0;
            foreach (var id in ids)
            {
                string old;
                var when = DateTime.Now;
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT {col}{(dateCol is null ? "" : $", {dateCol}")} FROM {table} WHERE {idCol} = @id";
                    Add(cmd, "@id", id);
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    if (!await r.ReadAsync(ct)) continue;
                    old = r.IsDBNull(0) ? "" : r.GetString(0);
                    if (dateCol is not null && !r.IsDBNull(1))
                    {
                        try { when = Convert.ToDateTime(r.GetValue(1)); } catch { /* 모르면 지금 */ }
                    }
                }

                var matches = DataString.Matches(old);
                if (matches.Count == 0) continue;

                if (dryRun)
                {
                    foreach (Match m in matches) { tFiles++; tBytes += Decode(m.Value)?.Data.Length ?? 0; }
                    tRecords++;
                    continue;
                }

                // 파일을 만들고 첨부 줄을 넣은 뒤, 칸을 바꾼다. 칸을 못 바꾸면 만든 것을 되돌린다.
                var made = new List<Attachment>();
                var replaced = new Dictionary<string, string>(StringComparer.Ordinal);
                var bad = false;
                try
                {
                    var n = 0;
                    foreach (Match m in matches)
                    {
                        if (replaced.ContainsKey(m.Value)) continue;
                        var d = Decode(m.Value);
                        if (d is null) { bad = true; continue; }   // 깨진 값은 그대로 둔다
                        n++;
                        var fileName = d.Name ?? $"{t.Label}{n}{d.Ext}";
                        using var ms = new MemoryStream(d.Data);
                        var row = await store.SaveAsync(ms, fileName, d.Mime, d.Data.Length, "migration",
                            t.Category, $"{t.Label}_{id}", when, ct);
                        row.Scope = t.Scope;
                        db.Attachments.Add(row);
                        await db.SaveChangesAsync(ct);
                        made.Add(row);
                        replaced[m.Value] = "\"" + AttachmentsController.ToDto(row).Ref + "\"";
                        tFiles++;
                        tBytes += d.Data.Length;
                    }
                    if (replaced.Count == 0) { if (bad) failed++; continue; }

                    var sb = new StringBuilder(old.Length);
                    var last = 0;
                    foreach (Match m in matches)
                    {
                        sb.Append(old, last, m.Index - last);
                        sb.Append(replaced.TryGetValue(m.Value, out var rep) ? rep : m.Value);
                        last = m.Index + m.Length;
                    }
                    sb.Append(old, last, old.Length - last);

                    int changed;
                    await using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"UPDATE {table} SET {col} = @new WHERE {idCol} = @id AND {col} = @old";
                        Add(cmd, "@new", sb.ToString());
                        Add(cmd, "@id", id);
                        Add(cmd, "@old", old);
                        changed = await cmd.ExecuteNonQueryAsync(ct);
                    }
                    if (changed == 0)
                    {
                        Undo(db, store, made);
                        tFiles -= made.Count;
                        tBytes -= made.Sum(a => a.Size);
                        skipped++;
                        Console.WriteLine($"[migrate] {t.Entity.Name}#{id} 은 그 사이 누가 고쳐서 건너뜀(다음 실행 때 다시).");
                        continue;
                    }
                    tRecords++;
                    if (bad) failed++;
                }
                catch (Exception ex)
                {
                    Undo(db, store, made);
                    tFiles -= made.Count;
                    tBytes -= made.Sum(a => a.Size);
                    failed++;
                    Console.WriteLine($"[migrate][오류] {t.Entity.Name}#{id} {t.Column}: {ex.Message}");
                }
            }
            Console.WriteLine($"[migrate] {t.Category}/{t.Label} ({t.Entity.Name}.{t.Column}): 기록 {tRecords}건, 파일 {tFiles}개, {tBytes / 1048576.0:0.0}MB");
            records += tRecords; files += tFiles; bytes += tBytes;
        }

        Console.WriteLine($"[migrate] DB 안의 사진 {(dryRun ? "옮길 대상" : "옮김")}: 기록 {records}건, 파일 {files}개, {bytes / 1048576.0:0.0}MB"
            + (skipped > 0 ? $", 건너뜀 {skipped}" : "") + (failed > 0 ? $", 실패·깨진 값 {failed}" : ""));

        var (renamed, missing, renameFailed) = await RenameLegacyAsync(db, store, dryRun, ct);
        Console.WriteLine($"[migrate] 옛 이름 첨부 {(dryRun ? "정리할 대상" : "정리")}: {renamed}개"
            + (missing > 0 ? $", 파일 없음 {missing}(예전 폴더를 새 위치로 복사했는지 확인)" : "")
            + (renameFailed > 0 ? $", 실패 {renameFailed}" : ""));
        return new Result(records, files, bytes, skipped, failed + renameFailed, renamed, missing);
    }

    private static readonly Regex LegacyFolder = new(@"^\d{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyName = new(@"^[0-9a-f]{32}(\.[^.]*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>옛 첨부의 분류. office 영역은 지금 BROKEN 만 첨부를 쓴다.</summary>
    private static string LegacyCategory(string scope) => scope.ToLowerInvariant() switch
    {
        "office" => "BROKEN",
        "field" => "체크시트",
        _ => AttachmentStore.CategoryOf(scope, null),
    };

    private static async Task<(int Renamed, int Missing, int Failed)> RenameLegacyAsync(
        CleanPotalDbContext db, AttachmentStore store, bool dryRun, CancellationToken ct)
    {
        int renamed = 0, missing = 0, failed = 0;
        var rows = (await db.Attachments.Where(a => a.Folder.Length == 6).ToListAsync(ct))
            .Where(a => LegacyFolder.IsMatch(a.Folder) && LegacyName.IsMatch(a.StoredName))
            .ToList();
        foreach (var a in rows)
        {
            var oldPath = store.PathOf(a);
            if (!File.Exists(oldPath)) { missing++; continue; }
            if (dryRun) { renamed++; continue; }

            string? newPath = null;
            var (oldFolder, oldStored) = (a.Folder, a.StoredName);
            try
            {
                var (folder, stored) = await store.CopyToNewNameAsync(a, LegacyCategory(a.Scope), a.CreatedAt, ct);
                newPath = Path.Combine(store.Root, folder, stored);
                a.Folder = folder;
                a.StoredName = stored;
                await db.SaveChangesAsync(ct);
                newPath = null;   // DB 가 새 자리를 가리킨다 — 이제 옛 파일을 지운다
                try { File.Delete(oldPath); } catch { /* 옛 파일이 남아도 화면은 새 자리를 본다 */ }
                renamed++;
            }
            catch (Exception ex)
            {
                if (newPath is not null) { try { File.Delete(newPath); } catch { /* 남아도 가리키는 곳이 없을 뿐 */ } }
                // 이 줄만 되돌린다(나머지 줄은 계속 추적해야 다음 저장이 먹는다).
                a.Folder = oldFolder;
                a.StoredName = oldStored;
                db.Entry(a).State = EntityState.Unchanged;
                failed++;
                Console.WriteLine($"[migrate][오류] 첨부 #{a.Id} 이름 정리 실패: {ex.Message}");
            }
        }
        return (renamed, missing, failed);
    }

    private static void Undo(CleanPotalDbContext db, AttachmentStore store, List<Attachment> made)
    {
        foreach (var a in made)
        {
            store.TryDelete(a);
            try { db.Attachments.Remove(a); } catch { /* 이미 떨어져 나갔으면 그대로 */ }
        }
        try { db.SaveChanges(); } catch { /* 첨부 줄이 남아도 어디서도 가리키지 않을 뿐이다 */ }
        made.Clear();
    }

    private sealed record Decoded(byte[] Data, string Mime, string? Name, string Ext);

    /// <summary>"data:형식[;name=이름];base64,내용" (JSON 문자열 그대로) → 바이트. 모양이 어긋나면 null.</summary>
    private static Decoded? Decode(string jsonString)
    {
        string url;
        try { url = JsonSerializer.Deserialize<string>(jsonString) ?? ""; }
        catch (JsonException) { return null; }
        var comma = url.IndexOf(',');
        if (!url.StartsWith("data:", StringComparison.Ordinal) || comma < 0) return null;

        var parts = url[5..comma].Split(';');
        if (!parts.Contains("base64", StringComparer.OrdinalIgnoreCase)) return null;
        var mime = parts[0].Trim().ToLowerInvariant();
        if (mime.Length == 0) mime = "application/octet-stream";
        string? name = null;
        foreach (var p in parts.Skip(1))
        {
            if (!p.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) continue;
            try { name = Uri.UnescapeDataString(p[5..]); } catch { name = p[5..]; }
        }

        byte[] data;
        try { data = Convert.FromBase64String(url[(comma + 1)..]); }
        catch (FormatException) { return null; }

        var ext = mime switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => Path.GetExtension(name ?? "") is { Length: > 0 } e ? e : ".bin",
        };
        if (string.IsNullOrWhiteSpace(name)) name = null;
        return new Decoded(data, mime, name, ext);
    }

    /// <summary>표·칸 이름은 모델에서 온 것이라 사용자 입력이 섞이지 않는다. [ ] 는 SQL Server·SQLite 모두 받는다.</summary>
    private static string Q(string name) => "[" + name.Replace("]", "]]") + "]";

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
