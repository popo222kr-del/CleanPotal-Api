using System.Text.Json;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 권한 프리셋(역할) — 관리자가 사용자 계정 관리 화면에서 만들고 고치고, 여러 사람에게 한 번에 적용한다.
/// 처음 열 때 표가 비어 있으면 기본 프리셋을 채운다(관리자가 모두 지운 뒤에도 다시 채워지지 않게, 한 번이라도 저장했으면 비어 있어도 둔다
/// — 저장할 때 목록이 비면 거부한다).
/// </summary>
public class PermissionPresetService
{
    private readonly CleanPotalDbContext _db;
    public PermissionPresetService(CleanPotalDbContext db) => _db = db;

    private static string J(params string[] routes) => JsonSerializer.Serialize(routes);

    /// <summary>기본 프리셋 — 2026-09-30 메뉴 재편 기준. 영역 등급은 가장 높게 필요한 등급, 편집하면 안 되는 메뉴만 조회만으로 내린다.</summary>
    public static IReadOnlyList<PermissionPreset> Defaults() => new List<PermissionPreset>
    {
        new() { Name = "현장 작업자", Description = "세정 작업·설비·공정·MES 편집(배차·스케줄 보드·ICP-MS·양식·공지는 조회), 일정·근무표·자재 조회, OFFICE 없음",
            AccessSchedule = 1, AccessRoster = 1, AccessHandover = 2, AccessField = 2, AccessMaterial = 1, AccessOffice = 0, AccessMes = 2,
            ReadOnlyMenus = J("/notice", "/dispatch", "/schedule-board", "/icpms", "/work/icpms", "/work/forms"), HiddenMenus = J() },
        new() { Name = "현장 리더", Description = "+ 일정·근무표·자재 편집, OFFICE 조회(견적서 숨김), ICP-MS (주간 분석)은 조회",
            AccessSchedule = 2, AccessRoster = 2, AccessHandover = 2, AccessField = 2, AccessMaterial = 2, AccessOffice = 1, AccessMes = 2,
            ReadOnlyMenus = J("/icpms"), HiddenMenus = J("/quotation") },
        new() { Name = "물류 담당", Description = "배차·기타/주간세정 출고·재고·폐기품·자재물류 일정 편집, 설비·공정은 조회, 업체 조회(견적서 숨김)",
            AccessSchedule = 2, AccessRoster = 1, AccessHandover = 2, AccessField = 1, AccessMaterial = 2, AccessOffice = 1, AccessMes = 1,
            ReadOnlyMenus = J("/schedule-board", "/meeting", "/notice"), HiddenMenus = J("/quotation", "/work-assignment") },
        new() { Name = "품질 담당", Description = "체크시트·ICP-MS·BROKEN·주간보고 편집, 약액·KOH·BAKE 기록은 조회, 세정 작업·자재 조회",
            AccessSchedule = 1, AccessRoster = 1, AccessHandover = 1, AccessField = 2, AccessMaterial = 1, AccessOffice = 2, AccessMes = 1,
            ReadOnlyMenus = J("/work/chemical", "/work/waste", "/work/bake", "/vendors"), HiddenMenus = J("/quotation") },
        new() { Name = "Office", Description = "전 영역 편집 (OFFICE 포함)",
            AccessSchedule = 2, AccessRoster = 2, AccessHandover = 2, AccessField = 2, AccessMaterial = 2, AccessOffice = 2, AccessMes = 2,
            ReadOnlyMenus = J(), HiddenMenus = J() },
        new() { Name = "조회 전용", Description = "전 영역 조회만 (OFFICE 없음)",
            AccessSchedule = 1, AccessRoster = 1, AccessHandover = 1, AccessField = 1, AccessMaterial = 1, AccessOffice = 0, AccessMes = 1,
            ReadOnlyMenus = J(), HiddenMenus = J() },
        new() { Name = "타 부서", Description = "나노세정 자료(세정 작업·설비·공정·자재·BROKEN)는 조회만, 부서별 자료(업체·견적서·주간보고·교육·업무 분장)는 자기 부서 편집",
            AccessSchedule = 1, AccessRoster = 1, AccessHandover = 1, AccessField = 1, AccessMaterial = 1, AccessOffice = 2, AccessMes = 1,
            ReadOnlyMenus = J("/broken"), HiddenMenus = J() },
    };

    private static PermissionPresetDto ToDto(PermissionPreset p) => new(p.Id, p.Name, p.Description, p.SortOrder,
        p.AccessSchedule, p.AccessRoster, p.AccessHandover, p.AccessField, p.AccessMaterial, p.AccessOffice, p.AccessMes,
        Routes(p.ReadOnlyMenus), Routes(p.HiddenMenus));

    /// <summary>메뉴 경로 JSON 배열 정리 — '/'로 시작하는 경로만, 중복 없이, 정렬.</summary>
    public static string Routes(string? json)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? new();
            return JsonSerializer.Serialize(arr.Where(s => !string.IsNullOrWhiteSpace(s) && s.StartsWith('/'))
                .Select(s => s.Trim()).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList());
        }
        catch (JsonException) { return "[]"; }
    }

    private static int Lv(int v) => Math.Clamp(v, 0, 2);

    public async Task<IReadOnlyList<PermissionPresetDto>> GetAllAsync()
    {
        if (!await _db.PermissionPresets.AnyAsync())
        {
            var i = 0;
            foreach (var d in Defaults()) { d.SortOrder = i++; d.UpdatedBy = "기본값"; _db.PermissionPresets.Add(d); }
            await _db.SaveChangesAsync();
        }
        return (await _db.PermissionPresets.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync())
            .Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PermissionPresetDto>> SaveAllAsync(IReadOnlyList<PermissionPresetDto> items, string by)
    {
        var clean = items.Select(x => x with { Name = (x.Name ?? "").Trim(), Description = (x.Description ?? "").Trim() }).ToList();
        if (clean.Count == 0) throw new BusinessRuleException("프리셋이 하나는 있어야 합니다.");
        if (clean.Any(x => x.Name.Length == 0)) throw new BusinessRuleException("프리셋 이름을 입력하세요.");
        if (clean.Any(x => x.Name.Length > 40)) throw new BusinessRuleException("프리셋 이름은 40자까지입니다.");
        var dup = clean.GroupBy(x => x.Name).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new BusinessRuleException($"프리셋 이름 '{dup.Key}' 이(가) 두 번 있습니다.");

        var existing = await _db.PermissionPresets.ToListAsync();
        var keep = clean.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        _db.PermissionPresets.RemoveRange(existing.Where(e => !keep.Contains(e.Id)));
        for (var i = 0; i < clean.Count; i++)
        {
            var x = clean[i];
            var e = x.Id > 0 ? existing.FirstOrDefault(p => p.Id == x.Id) : null;
            if (e is null) { e = new PermissionPreset(); _db.PermissionPresets.Add(e); }
            e.Name = x.Name;
            e.Description = x.Description.Length > 300 ? x.Description[..300] : x.Description;
            e.SortOrder = i;
            e.AccessSchedule = Lv(x.AccessSchedule); e.AccessRoster = Lv(x.AccessRoster); e.AccessHandover = Lv(x.AccessHandover);
            e.AccessField = Lv(x.AccessField); e.AccessMaterial = Lv(x.AccessMaterial); e.AccessOffice = Lv(x.AccessOffice);
            e.AccessMes = Lv(x.AccessMes);
            e.ReadOnlyMenus = Routes(x.ReadOnlyMenus);
            e.HiddenMenus = Routes(x.HiddenMenus);
            e.UpdatedBy = by;
            e.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return await GetAllAsync();
    }

    /// <summary>
    /// 여러 사람에게 프리셋 적용 — 영역 등급·조회만·숨김을 프리셋 값으로 바꾼다. 관리자 계정은 건너뛴다(관리자는 원래 전부 편집).
    /// 사람마다 변경 이력을 남긴다. 적용한 사람 수를 돌려준다.
    /// </summary>
    public async Task<int> ApplyAsync(int presetId, IReadOnlyList<int> userIds, string by)
    {
        var p = await _db.PermissionPresets.FindAsync(presetId) ?? throw new BusinessRuleException("프리셋을 찾을 수 없습니다. 화면을 새로 고쳐 보세요.");
        var ids = userIds.Distinct().ToList();
        var users = await _db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        var applied = 0;
        foreach (var u in users.Where(u => !u.IsAdmin && u.Username != "1004"))
        {
            u.AccessSchedule = p.AccessSchedule; u.AccessRoster = p.AccessRoster; u.AccessHandover = p.AccessHandover;
            u.AccessField = p.AccessField; u.AccessMaterial = p.AccessMaterial; u.AccessOffice = p.AccessOffice; u.AccessMes = p.AccessMes;
            u.ReadOnlyMenus = Routes(p.ReadOnlyMenus);
            u.HiddenMenus = Routes(p.HiddenMenus);
            _db.UserAuditLogs.Add(new UserAuditLog
            {
                TargetUser = $"{u.RealName}({u.Username})", Action = "권한 변경", Detail = $"프리셋 '{p.Name}' 적용", ByUser = by, CreatedAt = DateTime.Now,
            });
            applied++;
        }
        await _db.SaveChangesAsync();
        return applied;
    }
}
