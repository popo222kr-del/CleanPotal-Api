using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class EducationService : IEducationService
{
    private readonly CleanPotalDbContext _db;
    public EducationService(CleanPotalDbContext db) => _db = db;

    /// <summary>개인별 업무 분장표의 '외부 교육 기록'도 같은 변환을 쓴다.</summary>
    internal static EducationPlanDto ToDto(EducationPlan e) =>
        new(e.Id, e.MemberName, e.CourseName, e.StartDate, e.EndDate, e.Status, e.Progress, e.EduMethod, e.AttachmentPath);

    public async Task<IReadOnlyList<EducationPlanDto>> GetAllAsync(int? year, string? status, string? search)
    {
        var q = _db.EducationPlans.AsQueryable();
        if (year is not null) q = q.Where(e => e.StartDate != null && e.StartDate.Value.Year == year);
        if (!string.IsNullOrEmpty(status) && status != "전체") q = q.Where(e => e.Status == status);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(e => e.MemberName.Contains(search) || e.CourseName.Contains(search));
        var list = await q.OrderByDescending(e => e.StartDate).ThenBy(e => e.MemberName).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<EducationPlanDto> CreateAsync(EducationUpsertRequest r)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var e = new EducationPlan();
        Apply(e, r);
        _db.EducationPlans.Add(e);
        // 번호가 있어야 근무표 칸에 '어느 교육이 만든 칸인지' 적을 수 있어 먼저 저장한다.
        await _db.SaveChangesAsync();
        await SyncShiftsAsync(e, Scope(e));
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(e);
    }

    public async Task<EducationPlanDto?> UpdateAsync(int id, EducationUpsertRequest r)
    {
        var e = await _db.EducationPlans.FindAsync(id);
        if (e is null) return null;
        await using var tx = await _db.Database.BeginTransactionAsync();
        var before = Scope(e);
        Apply(e, r);
        var freed = await SyncShiftsAsync(e, before);
        await _db.SaveChangesAsync();
        await RefillOthersAsync(e.Id, freed);
        await tx.CommitAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var e = await _db.EducationPlans.FindAsync(id);
        if (e is null) return false;
        await using var tx = await _db.Database.BeginTransactionAsync();
        var owned = await OwnedShiftsAsync(e.Id, Scope(e));
        _db.ShiftSchedules.RemoveRange(owned);
        _db.EducationPlans.Remove(e);
        await _db.SaveChangesAsync();
        await RefillOthersAsync(e.Id, owned.Select(o => (o.MemberName, o.TargetDate)).ToList());
        await tx.CommitAsync();
        return true;
    }

    private static void Apply(EducationPlan e, EducationUpsertRequest r)
    {
        // 근무표와 이름으로 이어지므로 앞뒤 공백이 섞이면 아무도 보지 못하는 근무표 줄이 생긴다.
        e.MemberName = (r.MemberName ?? "").Trim();
        e.CourseName = (r.CourseName ?? "").Trim();
        e.StartDate = r.StartDate;
        e.EndDate = r.EndDate;
        e.Status = string.IsNullOrEmpty(r.Status) ? "대기" : r.Status;
        e.Progress = r.Progress;
        e.EduMethod = r.EduMethod;
        e.AttachmentPath = r.AttachmentPath ?? "";
    }

    // ── 근무 스케줄러 "교육" 자동 연동 ──
    //
    // 교육이 근무표에 만든 칸에는 CreatorName 에 "education:{교육번호}" 를 적는다. 그 표시가 있고 아직 '교육'인
    // 칸만 이 교육의 것으로 보고 고치거나 지운다. 예전 방식의 문제:
    //  - 수정할 때 지웠다 다시 넣는 과정에서 EF 가 지움 표시된 행을 그대로 돌려줘, 상태만 바꿔도 교육일이 사라졌다.
    //  - 연차·야간이 적힌 날도 '교육'으로 덮었고, 교육을 지우면 원래 근무가 돌아오지 않았다.
    //  - 지울 때 '교육'이기만 하면 지워서, 겹치는 다른 교육이나 사람이 직접 찍은 교육 도장까지 사라졌다.
    //  - 취소된 교육도 근무표에 올라갔다.
    // 이제는 빈 칸만 채우고, 자기 칸만 지우며, 취소면 올리지 않는다.

    private const string EduShiftType = "교육";
    private const string LegacyCreator = "education";
    private const int MaxDays = 366;
    private static string Marker(int planId) => $"education:{planId}";

    private readonly record struct PlanScope(string Member, DateOnly? Start, DateOnly? End);
    private static PlanScope Scope(EducationPlan e) => new(e.MemberName, e.StartDate, e.EndDate);

    /// <summary>근무표에 '교육'으로 올려야 할 날. 취소·기간 없음·기간이 비정상적으로 길면 없다.</summary>
    private static HashSet<DateOnly> WantedDates(EducationPlan e)
    {
        var set = new HashSet<DateOnly>();
        if (string.IsNullOrWhiteSpace(e.MemberName) || e.Status == "취소") return set;
        if (e.StartDate is not { } start || e.EndDate is not { } end || end < start) return set;
        if (end.DayNumber - start.DayNumber > MaxDays) return set;   // 연도 오타 등 — 수백 줄을 만들지 않는다
        for (var d = start; d <= end; d = d.AddDays(1)) set.Add(d);
        return set;
    }

    /// <summary>
    /// 이 교육이 만든 근무표 칸. 표시를 도입하기 전에 만든 칸은 "education" 으로만 적혀 있어 어느 교육 것인지
    /// 알 수 없으므로, 이 교육의 (이전) 이름·기간 안에 있는 것만 이 교육 것으로 본다.
    /// </summary>
    private async Task<List<ShiftSchedule>> OwnedShiftsAsync(int planId, PlanScope legacy)
    {
        var marker = Marker(planId);
        var owned = await _db.ShiftSchedules
            .Where(s => s.ShiftType == EduShiftType && s.CreatorName == marker)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(legacy.Member) && legacy.Start is { } ls && legacy.End is { } le && le >= ls)
        {
            owned.AddRange(await _db.ShiftSchedules
                .Where(s => s.ShiftType == EduShiftType && s.CreatorName == LegacyCreator
                            && s.MemberName == legacy.Member && s.TargetDate >= ls && s.TargetDate <= le)
                .ToListAsync());
        }
        return owned;
    }

    /// <summary>
    /// 교육의 현재 상태에 맞춰 근무표를 맞춘다. 자기 칸 가운데 필요 없어진 것은 지우고, 빈 날만 새로 채운다.
    /// 지운 칸(사람·날짜)을 돌려준다 — 겹치는 다른 교육이 그 날을 다시 채울 수 있게.
    /// </summary>
    private async Task<List<(string Member, DateOnly Date)>> SyncShiftsAsync(EducationPlan e, PlanScope before)
    {
        var marker = Marker(e.Id);
        var wanted = WantedDates(e);
        var freed = new List<(string Member, DateOnly Date)>();
        var kept = new HashSet<DateOnly>();

        foreach (var s in await OwnedShiftsAsync(e.Id, before))
        {
            if (s.MemberName == e.MemberName && wanted.Contains(s.TargetDate))
            {
                s.CreatorName = marker;   // 예전 방식 칸은 이 교육 표시로 옮겨 둔다
                kept.Add(s.TargetDate);
            }
            else
            {
                _db.ShiftSchedules.Remove(s);
                freed.Add((s.MemberName, s.TargetDate));
            }
        }

        var missing = wanted.Where(d => !kept.Contains(d)).ToList();
        if (missing.Count == 0) return freed;

        var from = missing.Min();
        var to = missing.Max();
        var existing = (await _db.ShiftSchedules
                .Where(s => s.MemberName == e.MemberName && s.TargetDate >= from && s.TargetDate <= to)
                .ToListAsync())
            .GroupBy(s => s.TargetDate)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var d in missing.OrderBy(d => d))
        {
            // 빈 칸만 채운다 — 연차·야간 등 이미 적힌 근무는 사람이 정한 것이라 덮지 않는다.
            // 근무표의 '비우기' 는 줄을 지우지 않고 "비우기" 로 남기므로(화면에는 빈 칸) 그 줄도 빈 날로 보고 그대로 고쳐 쓴다.
            if (existing.TryGetValue(d, out var row))
            {
                if (!IsBlank(row.ShiftType)) continue;
                row.ShiftType = EduShiftType;
                row.CreatorName = marker;
                row.CreateDate = DateTime.Now;
                continue;
            }
            _db.ShiftSchedules.Add(new ShiftSchedule
            {
                MemberName = e.MemberName, TargetDate = d, ShiftType = EduShiftType,
                TeamGroup = "", CreatorName = marker, CreateDate = DateTime.Now,
            });
        }
        return freed;
    }

    private static bool IsBlank(string? shiftType)
    {
        var t = (shiftType ?? "").Trim();
        return t.Length == 0 || t == "비우기";
    }

    /// <summary>비워진 날을 같은 사람의 다른 (겹치는) 교육이 다시 채우게 한다.</summary>
    private async Task RefillOthersAsync(int exceptPlanId, IReadOnlyCollection<(string Member, DateOnly Date)> freed)
    {
        if (freed.Count == 0) return;
        var members = freed.Select(f => f.Member).Distinct().ToList();
        var from = freed.Min(f => f.Date);
        var to = freed.Max(f => f.Date);
        var others = await _db.EducationPlans
            .Where(p => p.Id != exceptPlanId && members.Contains(p.MemberName)
                        && p.StartDate != null && p.EndDate != null
                        && p.StartDate <= to && p.EndDate >= from)
            .ToListAsync();
        if (others.Count == 0) return;
        foreach (var p in others)
        {
            // 한 교육씩 저장한다 — 겹치는 교육 둘이 같은 빈 날을 동시에 채우려다 (이름, 날짜) 고유 인덱스에 걸리지 않게.
            await SyncShiftsAsync(p, Scope(p));
            await _db.SaveChangesAsync();
        }
    }
}
