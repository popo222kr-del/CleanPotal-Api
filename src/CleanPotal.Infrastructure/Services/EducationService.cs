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

    private static EducationPlanDto ToDto(EducationPlan e) =>
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
        var e = new EducationPlan();
        Apply(e, r);
        _db.EducationPlans.Add(e);
        await AddEduShiftsAsync(e.MemberName, e.StartDate, e.EndDate);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<EducationPlanDto?> UpdateAsync(int id, EducationUpsertRequest r)
    {
        var e = await _db.EducationPlans.FindAsync(id);
        if (e is null) return null;
        // 이전 기간 교육 표시 제거 후 새 기간 반영 (스펙: 반드시 유지)
        await RemoveEduShiftsAsync(e.MemberName, e.StartDate, e.EndDate);
        Apply(e, r);
        await AddEduShiftsAsync(e.MemberName, e.StartDate, e.EndDate);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var e = await _db.EducationPlans.FindAsync(id);
        if (e is null) return false;
        await RemoveEduShiftsAsync(e.MemberName, e.StartDate, e.EndDate);
        _db.EducationPlans.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(EducationPlan e, EducationUpsertRequest r)
    {
        e.MemberName = r.MemberName;
        e.CourseName = r.CourseName;
        e.StartDate = r.StartDate;
        e.EndDate = r.EndDate;
        e.Status = string.IsNullOrEmpty(r.Status) ? "대기" : r.Status;
        e.Progress = r.Progress;
        e.EduMethod = r.EduMethod;
        e.AttachmentPath = r.AttachmentPath ?? "";
    }

    // ── 근무 스케줄러 "교육" 자동 연동 ──
    private async Task AddEduShiftsAsync(string member, DateOnly? start, DateOnly? end)
    {
        if (string.IsNullOrWhiteSpace(member) || start is null || end is null || end < start) return;
        var existing = await _db.ShiftSchedules
            .Where(s => s.MemberName == member && s.TargetDate >= start && s.TargetDate <= end).ToListAsync();
        var map = existing.ToDictionary(s => s.TargetDate);
        for (var d = start.Value; d <= end.Value; d = d.AddDays(1))
        {
            if (map.TryGetValue(d, out var s)) s.ShiftType = "교육";
            else _db.ShiftSchedules.Add(new ShiftSchedule
            {
                MemberName = member, TargetDate = d, ShiftType = "교육",
                TeamGroup = "", CreatorName = "education", CreateDate = DateTime.Now,
            });
        }
    }

    private async Task RemoveEduShiftsAsync(string member, DateOnly? start, DateOnly? end)
    {
        if (string.IsNullOrWhiteSpace(member) || start is null || end is null) return;
        var rows = await _db.ShiftSchedules
            .Where(s => s.MemberName == member && s.TargetDate >= start && s.TargetDate <= end && s.ShiftType == "교육")
            .ToListAsync();
        _db.ShiftSchedules.RemoveRange(rows);
    }
}
