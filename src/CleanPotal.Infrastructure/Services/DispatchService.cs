using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class DispatchService : IDispatchService
{
    private readonly CleanPotalDbContext _db;
    public DispatchService(CleanPotalDbContext db) => _db = db;

    private static DispatchDto ToDto(Dispatch d) => new(
        d.Id, d.VendorName, d.OutgoingDetails, d.IncomingDetails, d.ManagerName,
        d.ContactNumber, d.FullAddress, d.Note, d.CreateDate);

    public async Task<IReadOnlyList<DispatchDto>> GetAllAsync(string? search)
    {
        var q = _db.Dispatches.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(d => d.VendorName.Contains(search) || d.ManagerName.Contains(search) ||
                             d.FullAddress.Contains(search) || d.OutgoingDetails.Contains(search));
        var list = await q.OrderByDescending(d => d.CreateDate).ThenByDescending(d => d.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<DispatchDto> CreateAsync(DispatchUpsertRequest r)
    {
        var d = new Dispatch { CreateDate = DateTime.Now };
        Apply(d, r);
        _db.Dispatches.Add(d);
        await _db.SaveChangesAsync();
        return ToDto(d);
    }

    public async Task<DispatchDto?> UpdateAsync(int id, DispatchUpsertRequest r)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return null;
        Apply(d, r);
        await _db.SaveChangesAsync();
        return ToDto(d);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return false;
        _db.Dispatches.Remove(d);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(Dispatch d, DispatchUpsertRequest r)
    {
        d.VendorName = r.VendorName;
        d.OutgoingDetails = r.OutgoingDetails;
        d.IncomingDetails = r.IncomingDetails;
        d.ManagerName = r.ManagerName;
        d.ContactNumber = r.ContactNumber;
        d.FullAddress = r.FullAddress;
        d.Note = r.Note;
    }

    // ── 날짜별 배차표 (WPF와 동일: 대상 날짜를 CreateDate에 저장, 날짜 범위로 조회) ──

    private static (DateTime start, DateTime end) DayRange(DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        return (start, start.AddDays(1));
    }

    private static bool IsEmptyRow(DispatchRowRequest r) =>
        string.IsNullOrWhiteSpace(r.VendorName)
        && (string.IsNullOrWhiteSpace(r.OutgoingDetails) || r.OutgoingDetails.Trim() == "-")
        && string.IsNullOrWhiteSpace(r.IncomingDetails)
        && string.IsNullOrWhiteSpace(r.ManagerName)
        && string.IsNullOrWhiteSpace(r.ContactNumber)
        && string.IsNullOrWhiteSpace(r.FullAddress)
        && string.IsNullOrWhiteSpace(r.Note);

    private static void ApplyRow(Dispatch d, DispatchRowRequest r)
    {
        d.VendorName = r.VendorName.Trim();
        d.OutgoingDetails = string.IsNullOrWhiteSpace(r.OutgoingDetails) ? "-" : r.OutgoingDetails.Trim();
        d.IncomingDetails = r.IncomingDetails.Trim();
        d.ManagerName = r.ManagerName.Trim();
        d.ContactNumber = r.ContactNumber.Trim();
        d.FullAddress = r.FullAddress.Trim();
        d.Note = r.Note.Trim();
    }

    public async Task<IReadOnlyList<DispatchDto>> GetByDateAsync(DateOnly date)
    {
        var (start, end) = DayRange(date);
        var list = await _db.Dispatches
            .Where(d => d.CreateDate >= start && d.CreateDate < end)
            .OrderBy(d => d.Id)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<DispatchDto>> SaveDayAsync(DateOnly date, IReadOnlyList<DispatchRowRequest> rows)
    {
        var (start, end) = DayRange(date);
        var existing = await _db.Dispatches
            .Where(d => d.CreateDate >= start && d.CreateDate < end)
            .ToListAsync();

        // 요청에 없는 기존 행은 삭제 — 화면의 행 목록을 그날의 전체 상태로 본다
        // (빈 행은 저장 대상이 아니므로 keep 목록에서도 제외 → 비워서 보낸 행은 삭제됨)
        var keepIds = rows.Where(r => r.Id > 0 && !IsEmptyRow(r)).Select(r => r.Id).ToHashSet();
        _db.Dispatches.RemoveRange(existing.Where(d => !keepIds.Contains(d.Id)));

        var saved = new List<Dispatch>();
        foreach (var r in rows)
        {
            if (IsEmptyRow(r)) continue;
            Dispatch? d = null;
            if (r.Id > 0)
            {
                d = existing.FirstOrDefault(x => x.Id == r.Id)
                    // 다른 날짜로 옮겨진(이월 등) 행이면 중복 생성 대신 이 날짜로 다시 가져온다
                    ?? await _db.Dispatches.FindAsync(r.Id);
                if (d is not null) d.CreateDate = start.AddHours(12);
            }
            if (d is null) { d = new Dispatch { CreateDate = start.AddHours(12) }; _db.Dispatches.Add(d); }
            ApplyRow(d, r);
            saved.Add(d);
        }
        await _db.SaveChangesAsync();
        return saved.Select(ToDto).ToList();
    }

    public async Task<DispatchDto?> MoveAsync(int id, DateOnly targetDate)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return null;
        d.CreateDate = targetDate.ToDateTime(new TimeOnly(12, 0));
        await _db.SaveChangesAsync();
        return ToDto(d);
    }
}
