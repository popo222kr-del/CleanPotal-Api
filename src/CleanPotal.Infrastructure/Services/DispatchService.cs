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
        d.ContactNumber, d.FullAddress, d.Note, d.CreateDate, d.RowVersion);

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
        d.RowVersion++;
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

    /// <summary>
    /// 그날 배차표를 저장하고, 저장 뒤 그날의 전체 행(다른 사람이 그 사이 추가한 행 포함)을 돌려준다.
    ///
    /// 두 사람이 같은 날을 열어 두고 저장해도 서로의 행을 지우지 않게 한다.
    /// - 지우는 것은 이 화면이 알고 있던 행(knownIds) 가운데 요청에서 빠진 것뿐이다.
    /// - 요청의 행이 그날에 없으면(그 사이 다른 날로 이월됐거나 지워짐) 끌어오거나 되살리지 않는다.
    ///   예전에는 FindAsync 로 다른 날짜의 행을 이 날짜로 다시 끌고 와 이월이 취소됐다.
    /// </summary>
    public async Task<IReadOnlyList<DispatchDto>> SaveDayAsync(
        DateOnly date, IReadOnlyList<DispatchRowRequest> rows, IReadOnlyCollection<int>? knownIds = null)
    {
        var (start, end) = DayRange(date);
        var existing = await _db.Dispatches
            .Where(d => d.CreateDate >= start && d.CreateDate < end)
            .ToListAsync();

        // 빈 행은 저장 대상이 아니므로 keep 목록에서도 제외 → 비워서 보낸 행은 삭제됨
        var keepIds = rows.Where(r => r.Id > 0 && !IsEmptyRow(r)).Select(r => r.Id).ToHashSet();
        var known = knownIds?.ToHashSet();
        _db.Dispatches.RemoveRange(existing.Where(d => !keepIds.Contains(d.Id) && (known is null || known.Contains(d.Id))));

        foreach (var r in rows)
        {
            if (IsEmptyRow(r)) continue;
            Dispatch? d;
            if (r.Id > 0)
            {
                d = existing.FirstOrDefault(x => x.Id == r.Id);
                if (d is null) continue;
            }
            else
            {
                d = new Dispatch { CreateDate = start.AddHours(12) };
                _db.Dispatches.Add(d);
            }
            ApplyRow(d, r);
        }

        // 같은 행을 두 사람이 고친 경우 — 받아 간 버전이 지금과 다르고 이번에 값이 바뀌는 행이 있으면 막는다.
        // (값이 같으면 옛 화면이 그대로 다시 보낸 것이라 문제없다.) 바뀐 행은 버전을 올린다.
        _db.ChangeTracker.DetectChanges();
        var stale = new List<string>();
        foreach (var d in existing)
        {
            var entry = _db.Entry(d);
            if (entry.State != EntityState.Modified) continue;
            var r = rows.FirstOrDefault(x => x.Id == d.Id);
            if (r?.RowVersion is int v && v != d.RowVersion) stale.Add(string.IsNullOrWhiteSpace(d.VendorName) ? $"#{d.Id}" : d.VendorName);
            else d.RowVersion++;
        }
        if (stale.Count > 0)
            throw new CleanPotal.Core.ConcurrencyConflictException(
                $"그 사이 다른 사람이 먼저 고친 행이 있습니다: {string.Join(", ", stale)}. 새로 불러온 뒤 다시 저장하세요.");
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CleanPotal.Core.ConcurrencyConflictException("방금 다른 사람이 같은 날 배차표를 저장했습니다. 새로 불러온 뒤 다시 저장하세요.");
        }
        return await GetByDateAsync(date);
    }

    public async Task<DispatchDto?> MoveAsync(int id, DateOnly targetDate)
    {
        var d = await _db.Dispatches.FindAsync(id);
        if (d is null) return null;
        d.CreateDate = targetDate.ToDateTime(new TimeOnly(12, 0));
        d.RowVersion++;
        await _db.SaveChangesAsync();
        return ToDto(d);
    }
}
