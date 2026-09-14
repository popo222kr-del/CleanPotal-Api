using CleanPotal.Core;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 자료 변경 이력 기록과 동시 수정 충돌 처리 — 네 개 서비스(공지·인수인계·생산요청·생산미팅)가
/// 같은 방식으로 동작하도록 한곳에 모았다.
/// </summary>
public static class ContentAuditWriter
{
    /// <summary>이력 한 줄을 추가한다(SaveChanges 는 호출하는 쪽에서).</summary>
    public static void Add(CleanPotalDbContext db, ICurrentUser me, string entityType, int entityId, string action, string detail = "")
    {
        db.ContentAudits.Add(new ContentAudit
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Detail = Trim(detail),
            ByUserId = me.Id,
            ByUserName = me.RealName,
            CreatedAt = DateTime.Now,
        });
    }

    /// <summary>
    /// 클라이언트가 불러올 때 받은 버전과 현재 DB 버전을 대조한다.
    /// 버전을 안 보낸 요청(구버전 클라이언트)은 검사를 건너뛴다 — 막으면 기존 화면이 전부 멈추므로.
    /// </summary>
    public static void EnsureNotStale(int? clientVersion, int currentVersion, string what)
    {
        if (clientVersion is null) return;
        if (clientVersion.Value == currentVersion) return;
        throw new ConcurrencyConflictException(
            $"이 {what}은(는) 그 사이 다른 사용자가 먼저 수정했습니다. 새로고침해서 바뀐 내용을 확인한 뒤 다시 저장하세요.");
    }

    /// <summary>
    /// 저장하면서 동시 수정 충돌(EF 동시성 토큰 불일치)을 409 로 바꾼다.
    /// <see cref="EnsureNotStale"/> 이후 저장 직전의 아주 짧은 틈을 막는 2차 방어선이다.
    /// </summary>
    public static async Task SaveAsync(CleanPotalDbContext db, string what)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                $"이 {what}은(는) 방금 다른 사용자가 먼저 저장했습니다. 새로고침 후 다시 시도하세요.");
        }
    }

    /// <summary>변경 요약이 지나치게 길어지지 않게 자른다(본문 전체를 이력에 담지 않는다).</summary>
    private static string Trim(string s) => string.IsNullOrEmpty(s) || s.Length <= 500 ? (s ?? "") : s[..500] + "…";

    /// <summary>"제목: 전 → 후" 형태의 변경 요약을 만든다. 바뀐 항목만 남는다.</summary>
    public static string Describe(params (string Label, string? Before, string? After)[] fields)
    {
        var changed = fields
            .Where(f => !string.Equals(f.Before ?? "", f.After ?? "", StringComparison.Ordinal))
            .Select(f => $"{f.Label}: {Short(f.Before)} → {Short(f.After)}")
            .ToList();
        return changed.Count == 0 ? "변경 없음" : string.Join(" / ", changed);
    }

    private static string Short(string? s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length == 0) return "(비어 있음)";
        return s.Length <= 40 ? s : s[..40] + "…";
    }
}
