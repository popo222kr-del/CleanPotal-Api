using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 부서 칸(DeptId)이 생기기 전에 만든 업체·견적서·단가표·체크시트 구역·주간보고·교육 계획을 기본 부서로 채운다.
/// 지금까지 쓴 자료는 모두 나노세정 기준이므로 조직도의 '나노세정' 부서(없으면 이름에 '세정'이 들어간 첫 부서)로 둔다.
///
/// 교육 계획은 대상자의 부서가 조직도에 있으면 그 부서로 둔다(연구소 인원의 교육은 연구소가 본다).
/// 생산미팅(meeting)은 세정 공용이라 부서를 두지 않는다.
/// 빈 칸이 없으면 아무것도 바꾸지 않는다. 여러 번 실행해도 결과는 같다.
/// </summary>
public static class DeptBackfill
{
    public const string DefaultDeptName = "나노세정";

    public static int Run(CleanPotalDbContext db)
    {
        try
        {
            return RunCore(db);
        }
        catch (Exception ex)
        {
            // 채우지 못한 자료는 '부서 미지정'으로 누구나 보므로 기동을 막을 이유가 없다. 다음 기동 때 다시 한다.
            Console.WriteLine($"[dept][경고] 기존 자료 부서 채우기 실패(다음 기동 때 다시 시도): {ex.Message}");
            return 0;
        }
    }

    /// <summary>기본 부서(나노세정)의 조직도 Id. 조직도에 없으면 null.</summary>
    public static int? DefaultDeptId(CleanPotalDbContext db)
    {
        var depts = db.OrgUnits.AsNoTracking().Where(o => o.Kind == "dept")
            .OrderBy(o => o.OrderIndex).ThenBy(o => o.Id)
            .Select(o => new { o.Id, o.Name }).ToList();
        return depts.FirstOrDefault(d => d.Name.Trim() == DefaultDeptName)?.Id
               ?? depts.FirstOrDefault(d => d.Name.Contains("세정"))?.Id;
    }

    /// <summary>
    /// '부서별 자료' 를 켠 부서가 하나도 없으면(칸이 막 생긴 때) 기본 부서와 이미 자료가 있는 부서를 켠다.
    /// 한 곳이라도 켜져 있으면 관리자가 정한 것이므로 건드리지 않는다.
    /// </summary>
    private static void FlagDataDepts(CleanPotalDbContext db, int? defaultDept)
    {
        if (db.OrgUnits.Any(o => o.Kind == "dept" && o.UsesDeptData)) return;
        var used = new HashSet<int>();
        void Add(IEnumerable<int?> ids) { foreach (var i in ids) if (i is int v) used.Add(v); }
        Add(db.Vendors.Select(v => v.DeptId).Distinct().ToList());
        Add(db.Quotations.Select(q => q.DeptId).Distinct().ToList());
        Add(db.ProductMasters.Select(p => p.DeptId).Distinct().ToList());
        Add(db.CheckZones.Select(z => z.DeptId).Distinct().ToList());
        Add(db.Reports.Select(r => r.DeptId).Distinct().ToList());
        if (defaultDept is int d) used.Add(d);
        if (used.Count == 0) return;
        var n = db.OrgUnits.Where(o => o.Kind == "dept" && used.Contains(o.Id))
            .ExecuteUpdate(u => u.SetProperty(o => o.UsesDeptData, true));
        if (n > 0) Console.WriteLine($"[dept] '부서별 자료' 사용 부서 {n}곳을 켰습니다(조직 관리에서 바꿀 수 있습니다).");
    }

    private static int RunCore(CleanPotalDbContext db)
    {
        FlagDataDepts(db, DefaultDeptId(db));

        var pending = db.Vendors.Any(v => v.DeptId == null)
                      || db.Quotations.Any(q => q.DeptId == null)
                      || db.ProductMasters.Any(p => p.DeptId == null)
                      || db.CheckZones.Any(z => z.DeptId == null)
                      || db.Reports.Any(r => r.ReportType == "weekly" && r.DeptId == null)
                      || db.EducationPlans.Any(e => e.DeptId == null);
        if (!pending) return 0;

        if (DefaultDeptId(db) is not int dept)
        {
            Console.WriteLine($"[dept][경고] 조직도에 '{DefaultDeptName}' 부서가 없어 기존 자료의 부서를 채우지 못했습니다(부서 미지정 자료는 모두에게 보입니다).");
            return 0;
        }

        var n = 0;
        n += db.Vendors.Where(v => v.DeptId == null).ExecuteUpdate(u => u.SetProperty(v => v.DeptId, dept));
        n += db.Quotations.Where(q => q.DeptId == null).ExecuteUpdate(u => u.SetProperty(q => q.DeptId, dept));
        n += db.ProductMasters.Where(p => p.DeptId == null).ExecuteUpdate(u => u.SetProperty(p => p.DeptId, dept));
        n += db.CheckZones.Where(z => z.DeptId == null).ExecuteUpdate(u => u.SetProperty(z => z.DeptId, dept));
        n += db.Reports.Where(r => r.ReportType == "weekly" && r.DeptId == null)
            .ExecuteUpdate(u => u.SetProperty(r => r.DeptId, dept));

        // 교육: 대상자 부서가 조직도에 있으면 그 부서, 아니면 기본 부서.
        var deptIds = db.OrgUnits.AsNoTracking().Where(o => o.Kind == "dept").ToList()
            .GroupBy(o => o.Name.Trim()).ToDictionary(g => g.Key, g => g.First().Id);
        var memberDept = db.Users.AsNoTracking().Where(u => u.RealName != "")
            .Select(u => new { u.RealName, u.Department, u.IsResigned }).ToList()
            .OrderBy(u => u.IsResigned)   // 같은 이름이면 재직자 우선
            .GroupBy(u => u.RealName.Trim())
            .ToDictionary(g => g.Key, g => (g.First().Department ?? "").Trim());
        var plans = db.EducationPlans.Where(e => e.DeptId == null).ToList();
        foreach (var p in plans)
            p.DeptId = memberDept.TryGetValue(p.MemberName.Trim(), out var d) && deptIds.TryGetValue(d, out var id) ? id : dept;
        db.SaveChanges();
        n += plans.Count;

        if (n > 0) Console.WriteLine($"[dept] 부서 칸이 빈 기존 자료 {n}건을 채웠습니다(기본: {DefaultDeptName}).");
        return n;
    }
}
