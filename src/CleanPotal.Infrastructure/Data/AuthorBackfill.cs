using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 과거 자료의 작성자를 <b>이름 → 계정 ID</b> 로 채운다 (<c>backfill-authors</c> 명령).
///
/// 자동 실행하지 않고 명령으로만 도는 이유: 운영 데이터를 건드리는 작업이라
/// 담당자가 결과를 보고 판단할 수 있어야 하기 때문이다.
///
/// 안전 원칙:
/// - <c>CreatorUserId</c> 가 비어 있는 행만 채운다(이미 채워진 값은 건드리지 않는다).
/// - 이름이 <b>정확히 한 사람</b>과 일치할 때만 채운다.
///   동명이인이면 어느 쪽인지 사람이 정해야 하므로 건너뛰고 목록으로 출력한다.
/// - 이름으로 사람을 못 찾아도 지우거나 바꾸지 않는다. 그대로 둔다.
/// - 여러 번 실행해도 안전하다.
/// </summary>
public static class AuthorBackfill
{
    public static void Run(CleanPotalDbContext db)
    {
        // 실명 → 계정 ID. 같은 이름이 둘 이상이면 후보에서 제외한다(사람이 판단할 몫).
        var byName = db.Users
            .Where(u => u.RealName != "")
            .GroupBy(u => u.RealName)
            .Select(g => new { Name = g.Key, Ids = g.Select(x => x.Id).ToList() })
            .ToList();

        var unique = byName.Where(x => x.Ids.Count == 1).ToDictionary(x => x.Name, x => x.Ids[0], StringComparer.Ordinal);
        var duplicated = byName.Where(x => x.Ids.Count > 1).Select(x => x.Name).ToList();

        if (duplicated.Count > 0)
            Console.WriteLine($"[backfill] 동명이인이라 자동 매칭에서 제외: {string.Join(", ", duplicated)}");

        var unmatched = new SortedSet<string>(StringComparer.Ordinal);
        var filled = 0;

        int Fill<T>(IQueryable<T> rows, Func<T, string> nameOf, Action<T, int> setId) where T : class
        {
            var n = 0;
            foreach (var row in rows.ToList())
            {
                var name = (nameOf(row) ?? "").Trim();
                if (name.Length == 0) continue;
                if (unique.TryGetValue(name, out var uid)) { setId(row, uid); n++; }
                else unmatched.Add(name);
            }
            return n;
        }

        filled += Fill(db.Notices.Where(x => x.CreatorUserId == null),
                       x => x.Author, (x, id) => x.CreatorUserId = id);
        filled += Fill(db.Handovers.Where(x => x.CreatorUserId == null),
                       x => x.CreatorName, (x, id) => x.CreatorUserId = id);
        filled += Fill(db.ProductionMeetings.Where(x => x.CreatorUserId == null),
                       x => x.CreatorName, (x, id) => x.CreatorUserId = id);
        filled += Fill(db.ProdReqs.Where(x => x.CreatorUserId == null),
                       x => x.Requester, (x, id) => x.CreatorUserId = id);

        db.SaveChanges();

        Console.WriteLine($"[backfill] 작성자 계정 ID 를 채운 행: {filled}건");
        if (unmatched.Count > 0)
        {
            Console.WriteLine($"[backfill] 계정을 찾지 못해 그대로 둔 작성자 이름 {unmatched.Count}개:");
            Console.WriteLine($"           {string.Join(", ", unmatched)}");
            Console.WriteLine("           (퇴사 후 계정 삭제, 이름 표기 차이, 동명이인 등. 해당 행은 예전처럼 이름으로 대조합니다.)");
        }
    }
}
