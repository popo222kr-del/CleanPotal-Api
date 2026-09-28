using System.Linq.Expressions;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 부서별로 따로 관리하는 자료(업체·견적서·단가표·체크시트·주간보고·교육 현황·업무 분장)의 범위.
///
/// - 관리자가 아니면 **자기 부서 자료만** 보고, 등록하면 자기 부서로 정해지며, 다른 부서 자료는 고치거나 지우지 못한다.
///   (세정에서 연구소 자료를 볼 수도 만들 수도 없다.)
/// - 관리자는 모든 부서를 본다. 등록할 때 부서를 고르지 않으면 관리자 본인 부서.
/// - 부서는 조직도(OrgUnits, Kind=dept)의 Id 로 둔다 — 부서 이름을 바꿔도 자료가 따라간다.
///   부서 칸이 비어 있는 옛 자료는 시작할 때 기본 부서(나노세정)로 채운다(DeptBackfill).
///   그래도 비어 있는 자료(부서 미지정)는 누구나 본다 — 기본 부서를 못 찾았을 때 자료가 통째로 사라져 보이지 않게.
///
/// 로그인 사용자 없이 만든 서비스(테스트·가져오기)는 범위를 두지 않는다.
/// </summary>
public sealed class DeptScope
{
    private readonly CleanPotalDbContext _db;
    private readonly ICurrentUser? _me;
    private int? _mine;
    private bool _loaded;

    public DeptScope(CleanPotalDbContext db, ICurrentUser? me) { _db = db; _me = me; }

    /// <summary>범위 제한이 없다(관리자이거나 로그인 사용자 없이 만든 서비스).</summary>
    public bool Unrestricted => _me is null || _me.IsAdmin;

    /// <summary>내 부서(조직도 Id). 소속 부서가 조직도에 없으면 null.</summary>
    public async Task<int?> MyDeptIdAsync()
    {
        if (_loaded) return _mine;
        var name = (_me?.Department ?? "").Trim();
        _mine = name.Length == 0 ? null
            : await _db.OrgUnits.Where(o => o.Kind == "dept" && o.Name == name).Select(o => (int?)o.Id).FirstOrDefaultAsync();
        _loaded = true;
        return _mine;
    }

    /// <summary>
    /// 목록을 내 부서(+ 부서 미지정)로 좁힌다. 관리자는 그대로(모두) — 단 <paramref name="mineOnly"/> 면 관리자도 자기 부서만
    /// (기타세정 현황·배차표처럼 한 부서 업무 화면에서 쓰는 업체 목록).
    /// </summary>
    public async Task<IQueryable<T>> FilterAsync<T>(IQueryable<T> q, Expression<Func<T, int?>> dept, bool mineOnly = false)
    {
        if (Unrestricted && !(mineOnly && _me is not null)) return q;
        var mine = await MyDeptIdAsync();
        if (mine is null) return Unrestricted ? q : q.Where(_ => false);
        var holder = new DeptHolder(mine);
        var body = Expression.OrElse(
            Expression.Equal(dept.Body, Expression.Property(Expression.Constant(holder), nameof(DeptHolder.Value))),
            Expression.Equal(dept.Body, Expression.Constant(null, typeof(int?))));
        return q.Where(Expression.Lambda<Func<T, bool>>(body, dept.Parameters));
    }

    private sealed record DeptHolder(int? Value);

    /// <summary>
    /// 새 자료의 부서. 관리자는 고른 부서(없으면 본인 부서), 그 밖에는 자기 부서로 정해진다.
    /// 소속 부서가 조직도에 없으면 등록하지 못한다(어느 부서 자료인지 모르는 자료가 생기면 아무도 못 본다).
    /// </summary>
    public async Task<int?> ForCreateAsync(int? requested = null)
    {
        if (Unrestricted)
        {
            if (requested is int r && await _db.OrgUnits.AnyAsync(o => o.Id == r && o.Kind == "dept")) return r;
            return await MyDeptIdAsync();
        }
        return await MyDeptIdAsync()
               ?? throw new BusinessRuleException("소속 부서가 조직도에 없어 등록할 수 없습니다. 관리자에게 부서 등록을 요청하세요.");
    }

    /// <summary>
    /// 이 자료를 보거나 고칠 수 있는가 — 다른 부서 자료면 403. 관리자는 통과.
    /// 부서 칸이 빈 옛 자료(아직 채우기 전)는 막지 않는다.
    /// </summary>
    public async Task EnsureAsync(int? rowDept, string what = "자료")
    {
        if (Unrestricted || rowDept is null) return;
        if (await MyDeptIdAsync() != rowDept)
            throw new ForbiddenException($"다른 부서의 {what}입니다. 볼 수도 고칠 수도 없습니다.");
    }

    /// <summary>화면에 보여 줄 부서 이름표(Id → 이름). '등록 부서' 표시용.</summary>
    public async Task<Dictionary<int, string>> NamesAsync()
        => _names ??= await _db.OrgUnits.Where(o => o.Kind == "dept").ToDictionaryAsync(o => o.Id, o => o.Name);

    private Dictionary<int, string>? _names;

    /// <summary>
    /// 자료를 나눠 둘 부서 목록(등록 부서 고르기·거르기용). 조직 관리에서 '부서별 자료' 를 켠 부서만 —
    /// 조직도의 부서를 다 내보내면 팀처럼 쓰는 부서까지 칩이 늘어 난잡하다. 내 부서는 늘 넣는다(내 부서 표시에 쓴다).
    /// 쓰지 않는 부서와 관리자 계정만 있는 부서(예: 시스템 관리)는 뺀다. Mine = 내 부서.
    /// </summary>
    public async Task<IReadOnlyList<CalendarDeptDto>> ListAsync()
    {
        var units = await _db.OrgUnits.AsNoTracking().Where(o => o.Kind == "dept" && o.IsActive)
            .OrderBy(o => o.OrderIndex).ThenBy(o => o.Name).ToListAsync();
        var flags = await _db.Users.AsNoTracking().Where(u => !u.IsResigned)
            .Select(u => new { u.Department, u.IsAdmin }).ToListAsync();
        var adminOnly = flags.GroupBy(u => (u.Department ?? "").Trim())
            .Where(g => g.All(u => u.IsAdmin)).Select(g => g.Key).ToHashSet();
        var mine = await MyDeptIdAsync();
        return units.Where(o => o.Id == mine || (o.UsesDeptData && !adminOnly.Contains(o.Name.Trim())))
            .Select(o => new CalendarDeptDto(o.Id, o.Name,
                DeptPalette.ResolveShortName(o.ShortName, o.Name), DeptPalette.Resolve(o.Color, o.Id), Mine: o.Id == mine))
            .ToList();
    }

    /// <summary>부서 Id 의 이름(없으면 빈 칸). <see cref="NamesAsync"/> 를 먼저 불러 둔 뒤 쓴다.</summary>
    public string NameOf(int? id) => id is int i && _names is not null && _names.TryGetValue(i, out var n) ? n : "";
}
