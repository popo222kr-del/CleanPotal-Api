using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 영역(Scope) 칸이 생기기 전에 올린 첨부에 영역을 채운다.
///
/// 첨부는 기록 칸에 "att:{번호}|이름|종류" 로만 남으므로, 주간보고·생산미팅 칸에서 찾은 번호는 reports,
/// BROKEN 칸에서 찾은 번호는 office 로 본다. 두 곳에서 모두 쓰이거나 어디서도 찾지 못한 첨부는 빈 칸으로 두어
/// 예전처럼 로그인만 확인한다(잘못 막아서 볼 사람이 못 보는 것보다 낫다).
///
/// 빈 칸 첨부가 없으면 아무것도 읽지 않는다. 여러 번 실행해도 결과는 같다.
/// </summary>
public static class AttachmentScopeBackfill
{
    private static readonly Regex RefPattern = new(@"att:(\d+)\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int Run(CleanPotalDbContext db)
    {
        try
        {
            return RunCore(db);
        }
        catch (Exception ex)
        {
            // 채우지 못해도 빈 칸 첨부는 예전처럼 로그인만 확인하므로 기동을 막을 이유가 없다. 다음 기동 때 다시 한다.
            Console.WriteLine($"[attach][경고] 기존 첨부 영역 채우기 실패(다음 기동 때 다시 시도): {ex.Message}");
            return 0;
        }
    }

    private static int RunCore(CleanPotalDbContext db)
    {
        if (!db.Attachments.Any(a => a.Scope == "")) return 0;

        var scopes = new Dictionary<int, string>();
        void Collect(IEnumerable<string?> texts, string scope)
        {
            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text)) continue;
                foreach (Match m in RefPattern.Matches(text))
                {
                    if (!int.TryParse(m.Groups[1].Value, out var id)) continue;
                    // 서로 다른 영역에서 같이 쓰이면 어느 한쪽으로 막을 수 없으므로 표시해 두고 비워 둔다.
                    scopes[id] = scopes.TryGetValue(id, out var cur) && cur != scope ? "" : scope;
                }
            }
        }

        Collect(db.Reports.AsNoTracking().Select(r => r.MemoAttachments + "\n" + r.MainAttachments).ToList(), "reports");
        Collect(db.ReportBlocks.AsNoTracking().Select(b => b.FollowUpAttachments).ToList(), "reports");
        Collect(db.BrokenRecords.AsNoTracking()
            .Select(r => r.IncidentReports + "\n" + r.CountermeasureReports + "\n" + r.TrainingDocs + "\n" + r.TrainingImages)
            .ToList(), "office");
        Collect(db.BrokenTrainings.AsNoTracking().Select(t => t.Documents + "\n" + t.Images).ToList(), "office");

        var targets = scopes.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (targets.Count == 0) return 0;

        // 번호를 한꺼번에 넘기면 SQL Server 매개변수 상한(2100)을 넘어 다른 방식(OPENJSON)으로 바뀐다 — 나눠서 읽는다.
        var updated = 0;
        foreach (var chunk in targets.Keys.Chunk(500))
        {
            var rows = db.Attachments.Where(a => a.Scope == "" && chunk.Contains(a.Id)).ToList();
            foreach (var a in rows) a.Scope = targets[a.Id];
            db.SaveChanges();
            updated += rows.Count;
        }
        if (updated > 0)
            Console.WriteLine($"[attach] 기존 첨부 {updated}건에 영역을 채웠습니다(받을 때 그 화면의 조회 권한을 확인).");
        return updated;
    }
}
