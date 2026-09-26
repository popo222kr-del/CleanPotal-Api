using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 자재물류 일정 현황 서비스.
/// 하루 저장은 "해당 날짜 엔트리 전체 교체(마지막 저장 우선)" 방식으로, 동시 편집은 마지막 저장이 이긴다.
/// </summary>
public class MaterialService : IMaterialService
{
    private readonly CleanPotalDbContext _db;
    public MaterialService(CleanPotalDbContext db) => _db = db;

    // 고정 차량 5대 — 헤더: 5t(2255)·5t(5907)·3.5t(5335)·1t(0765)·1t(4795)
    private static readonly MaterialVehicleDto[] _vehicles =
    {
        new("v1", "5t (2255)"),
        new("v2", "5t (5907)"),
        new("v3", "3.5t (5335)"),
        new("v4", "1t (0765)"),
        new("v5", "1t (4795)"),
    };

    public IReadOnlyList<MaterialVehicleDto> Vehicles => _vehicles;

    private static List<string> SplitVehicles(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public async Task<MaterialDayDto> GetDayAsync(DateOnly date)
    {
        var roster = await _db.MaterialRosterMembers.OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => m.Name).ToListAsync();

        var entries = await _db.MaterialScheduleEntries.Where(e => e.TargetDate == date).ToListAsync();
        var map = entries.ToDictionary(e => (e.PersonName, e.Period), e => e);

        MaterialCellDto Cell(string person, string period) =>
            map.TryGetValue((person, period), out var e)
                ? new MaterialCellDto(e.Destination, SplitVehicles(e.Vehicles))
                : new MaterialCellDto("", new List<string>());

        var rows = roster
            .Select(name => new MaterialRowDto(name, Cell(name, "AM"), Cell(name, "PM")))
            .ToList();

        var note = await _db.MaterialDayNotes.FirstOrDefaultAsync(n => n.TargetDate == date);

        return new MaterialDayDto(date, roster, _vehicles, rows, note?.NoteAm ?? "", note?.NotePm ?? "", note?.RowVersion ?? 0);
    }

    public async Task<MaterialDayDto> SaveDayAsync(DateOnly date, MaterialSaveRequest req)
    {
        // 유효 로스터 이름만 반영 (탈락한 담당자 잔재 방지)
        var rosterNames = await _db.MaterialRosterMembers.Select(m => m.Name).ToListAsync();
        var validNames = rosterNames.ToHashSet();

        // 삭제와 다시 넣기를 한 트랜잭션으로 묶는다. 예전에는 삭제를 먼저 커밋한 뒤 넣다가 실패하면(같은 사람이
        // 두 번 들어와 고유 인덱스에 걸리는 등) 그날 일정이 통째로 사라졌다.
        await using var tx = await _db.Database.BeginTransactionAsync();

        // 하루를 통째로 바꾸므로, 두 사람이 같은 날을 열어 두면 뒤 사람이 앞 사람 것을 모두 지웠다 — 버전을 확인한다.
        var dayNote = await _db.MaterialDayNotes.FirstOrDefaultAsync(n => n.TargetDate == date);
        ContentAuditWriter.EnsureNotStale(req.Version, dayNote?.RowVersion ?? 0, "날짜의 자재 일정");

        // 해당 날짜 엔트리 전체 교체
        var existing = await _db.MaterialScheduleEntries.Where(e => e.TargetDate == date).ToListAsync();
        _db.MaterialScheduleEntries.RemoveRange(existing);
        // 삭제를 먼저 커밋해 (TargetDate, PersonName, Period) 유니크 인덱스와 재삽입이 충돌하지 않게 한다.
        await _db.SaveChangesAsync();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in req.Rows ?? new List<MaterialRowInput>())
        {
            if (!validNames.Contains(row.Person)) continue;
            if (!seen.Add(row.Person)) continue;   // 같은 사람이 두 줄이면 첫 줄만 — (날짜, 사람, 오전/오후) 는 하나뿐이다
            AddCell(date, row.Person, "AM", row.Am);
            AddCell(date, row.Person, "PM", row.Pm);
        }

        // 특이사항 upsert
        var note = await _db.MaterialDayNotes.FirstOrDefaultAsync(n => n.TargetDate == date);
        if (note is null)
        {
            note = new MaterialDayNote { TargetDate = date };
            _db.MaterialDayNotes.Add(note);
        }
        note.NoteAm = req.NoteAm ?? "";
        note.NotePm = req.NotePm ?? "";
        note.RowVersion++;

        await ContentAuditWriter.SaveAsync(_db, "날짜의 자재 일정");
        await tx.CommitAsync();
        return await GetDayAsync(date);
    }

    private void AddCell(DateOnly date, string person, string period, MaterialCellInput? cell)
    {
        var dest = cell?.Destination?.Trim() ?? "";
        var vehicles = (cell?.Vehicles ?? new List<string>())
            .Where(v => _vehicles.Any(x => x.Key == v)).Distinct().ToList();
        if (dest.Length == 0 && vehicles.Count == 0) return;   // 빈 칸은 저장하지 않음

        _db.MaterialScheduleEntries.Add(new MaterialScheduleEntry
        {
            TargetDate = date,
            PersonName = person,
            Period = period,
            Destination = dest,
            Vehicles = string.Join(",", vehicles),
        });
    }

    public async Task<IReadOnlyList<string>> GetRosterAsync() =>
        await _db.MaterialRosterMembers.OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => m.Name).ToListAsync();

    public async Task<IReadOnlyList<string>> SaveRosterAsync(MaterialRosterSaveRequest req)
    {
        // 순서/이름을 통째로 교체 (추가·삭제·순서변경·이름수정 일괄)
        // 같은 이름을 두 번 넣으면 그날 일정 저장이 한 사람을 두 줄로 받게 된다 — 명단에서부터 한 번만 둔다.
        var names = (req.Names ?? new List<string>())
            .Select(n => n?.Trim() ?? "")
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _db.MaterialRosterMembers.RemoveRange(_db.MaterialRosterMembers);
        for (int i = 0; i < names.Count; i++)
            _db.MaterialRosterMembers.Add(new MaterialRosterMember { Name = names[i], SortOrder = i });

        await _db.SaveChangesAsync();
        return names;
    }

    public async Task<IReadOnlyList<MaterialDestinationDto>> GetDestinationsAsync()
    {
        var result = new List<MaterialDestinationDto>();
        var seen = new HashSet<string>();

        // 실제 배차(dispatch) 먼저 — 표시엔 주소, 입력값은 업체명만 (스펙: 배차표에서 불러오기)
        var dispatches = await _db.Dispatches.OrderByDescending(d => d.CreateDate).ThenByDescending(d => d.Id).ToListAsync();
        foreach (var d in dispatches)
        {
            if (string.IsNullOrWhiteSpace(d.VendorName) || !seen.Add(d.VendorName)) continue;
            var addr = !string.IsNullOrWhiteSpace(d.FullAddress) ? d.FullAddress
                     : !string.IsNullOrWhiteSpace(d.OutgoingDetails) ? d.OutgoingDetails : "";
            result.Add(new MaterialDestinationDto(d.VendorName, addr));
        }

        // 이어서 업체 마스터
        var vendors = await _db.Vendors.OrderBy(v => v.VendorName).ToListAsync();
        foreach (var v in vendors)
        {
            if (seen.Add(v.VendorName))
                result.Add(new MaterialDestinationDto(v.VendorName, v.Note));
        }

        var pastDest = await _db.MaterialScheduleEntries
            .Where(e => e.Destination != "")
            .Select(e => e.Destination).Distinct().ToListAsync();
        foreach (var d in pastDest.OrderBy(x => x))
        {
            if (seen.Add(d))
                result.Add(new MaterialDestinationDto(d, ""));
        }
        return result;
    }
}
