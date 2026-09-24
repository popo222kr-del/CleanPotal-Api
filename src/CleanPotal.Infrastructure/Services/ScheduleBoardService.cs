using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ScheduleBoardService : IScheduleBoardService
{
    private readonly CleanPotalDbContext _db;
    public ScheduleBoardService(CleanPotalDbContext db) => _db = db;

    // ── 설비 묶음(MDC · MSC · NDC …) ──────────────────────────────────────

    /// <summary>
    /// 묶음 목록. 표가 비어 있으면 지금 설비들이 쓰고 있는 이름으로 채운다.
    ///
    /// 이 기능을 넣기 전에는 묶음이 화면에 박혀 있었고 설비만 이름을 들고 있었다. 그대로 두면
    /// 처음 열었을 때 목록이 비어 기존 묶음이 사라진 것처럼 보인다.
    /// </summary>
    public async Task<IReadOnlyList<ScheduleGroupDto>> GetGroupsAsync()
    {
        if (!await _db.ScheduleEquipGroups.AnyAsync())
        {
            var used = (await _db.ScheduleEquipments.Where(e => e.IsActive)
                    .Select(e => e.GroupName).Distinct().ToListAsync())
                .Select(n => (n ?? "").Trim())
                .Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            if (used.Count == 0) used = new List<string> { "MDC", "MSC", "NDC" };

            for (var i = 0; i < used.Count; i++)
                _db.ScheduleEquipGroups.Add(new ScheduleEquipGroup { Name = used[i], OrderIndex = i });
            await _db.SaveChangesAsync();
        }

        var groups = await _db.ScheduleEquipGroups.OrderBy(g => g.OrderIndex).ThenBy(g => g.Id).ToListAsync();
        var counts = (await _db.ScheduleEquipments.Where(e => e.IsActive)
                .Select(e => e.GroupName).ToListAsync())
            .GroupBy(n => (n ?? "").Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return groups.Select(g => new ScheduleGroupDto(
            g.Id, g.Name, g.OrderIndex, counts.GetValueOrDefault(g.Name, 0))).ToList();
    }

    public async Task<string?> AddGroupAsync(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "묶음 이름을 입력하세요.";
        if (name.Length > 40) return "묶음 이름은 40자를 넘을 수 없습니다.";
        await GetGroupsAsync();   // 표가 비어 있으면 먼저 채운다
        if (await _db.ScheduleEquipGroups.AnyAsync(g => g.Name == name)) return $"이미 있는 묶음입니다: {name}";

        var maxOrder = await _db.ScheduleEquipGroups.Select(g => (int?)g.OrderIndex).MaxAsync() ?? -1;
        _db.ScheduleEquipGroups.Add(new ScheduleEquipGroup { Name = name, OrderIndex = maxOrder + 1 });
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>이름을 바꾸면 그 이름을 쓰던 설비도 같이 바꾼다 — 설비는 이름을 문자열로 들고 있다.</summary>
    public async Task<string?> RenameGroupAsync(int id, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "묶음 이름을 입력하세요.";
        if (name.Length > 40) return "묶음 이름은 40자를 넘을 수 없습니다.";

        var g = await _db.ScheduleEquipGroups.FindAsync(id);
        if (g is null) return "없는 묶음입니다.";
        if (g.Name == name) return null;
        if (await _db.ScheduleEquipGroups.AnyAsync(x => x.Name == name && x.Id != id)) return $"이미 있는 묶음입니다: {name}";

        var old = g.Name;
        g.Name = name;
        foreach (var e in await _db.ScheduleEquipments.Where(e => e.GroupName == old).ToListAsync())
            e.GroupName = name;

        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>쓰고 있는 설비가 있으면 지우지 않는다 — 지우면 그 설비의 묶음이 떠 버린다.</summary>
    public async Task<string?> DeleteGroupAsync(int id)
    {
        var g = await _db.ScheduleEquipGroups.FindAsync(id);
        if (g is null) return "없는 묶음입니다.";

        var used = await _db.ScheduleEquipments.CountAsync(e => e.IsActive && e.GroupName == g.Name);
        if (used > 0) return $"설비 {used}대가 '{g.Name}' 을(를) 쓰고 있습니다. 먼저 다른 묶음으로 옮기세요.";

        _db.ScheduleEquipGroups.Remove(g);
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task ReorderGroupsAsync(IReadOnlyList<int> ids)
    {
        var groups = await _db.ScheduleEquipGroups.ToDictionaryAsync(g => g.Id);
        for (var i = 0; i < ids.Count; i++)
            if (groups.TryGetValue(ids[i], out var g)) g.OrderIndex = i;
        await _db.SaveChangesAsync();
    }

    // ── 설비 (DB 마스터) — Index=Slot(블록 참조), OrderIndex=표시순서 ──
    // 표시명 = 설비명 (공정) (특이사항) — 비어 있는 항목은 생략
    private static string EquipDisplay(ScheduleEquipment e)
    {
        var s = e.Name?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(e.Process)) s += $" ({e.Process.Trim()})";
        if (!string.IsNullOrWhiteSpace(e.Note)) s += $" ({e.Note.Trim()})";
        return s;
    }

    private static ScheduleEquipmentDto EquipDto(ScheduleEquipment e) =>
        new(e.Slot, EquipDisplay(e), e.Id, e.GroupName, e.OrderIndex, e.Name, e.Process, e.Note, e.IsIdle);

    public async Task<IReadOnlyList<ScheduleEquipmentDto>> GetEquipmentsAsync()
    {
        var list = await _db.ScheduleEquipments.Where(e => e.IsActive)
            .OrderBy(e => e.OrderIndex).ThenBy(e => e.Id).ToListAsync();
        return list.Select(EquipDto).ToList();
    }

    public async Task<ScheduleEquipmentDto> AddEquipmentAsync(string name, string groupName, string process, string note, bool isIdle)
    {
        var maxSlot = await _db.ScheduleEquipments.Select(e => (int?)e.Slot).MaxAsync() ?? -1;
        var maxOrder = await _db.ScheduleEquipments.Where(e => e.IsActive).Select(e => (int?)e.OrderIndex).MaxAsync() ?? -1;
        var e = new ScheduleEquipment
        {
            Name = (name ?? "").Trim(),
            Process = (process ?? "").Trim(),
            Note = (note ?? "").Trim(),
            GroupName = string.IsNullOrWhiteSpace(groupName) ? "MDC" : groupName.Trim(),
            IsIdle = isIdle,
            Slot = maxSlot + 1, OrderIndex = maxOrder + 1, IsActive = true,
        };
        _db.ScheduleEquipments.Add(e);
        await _db.SaveChangesAsync();
        return EquipDto(e);
    }

    public async Task<ScheduleEquipmentDto?> UpdateEquipmentAsync(int id, string name, string groupName, string process, string note, bool isIdle)
    {
        var e = await _db.ScheduleEquipments.FindAsync(id);
        if (e is null) return null;
        if (!string.IsNullOrWhiteSpace(name)) e.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(groupName)) e.GroupName = groupName.Trim();
        e.Process = (process ?? "").Trim();   // 공정·특이사항은 비울 수 있음
        e.Note = (note ?? "").Trim();
        e.IsIdle = isIdle;
        await _db.SaveChangesAsync();
        return EquipDto(e);
    }

    public async Task<bool> DeleteEquipmentAsync(int id)
    {
        var e = await _db.ScheduleEquipments.FindAsync(id);
        if (e is null) return false;
        e.IsActive = false;   // 소프트 삭제 → 기존 배치(Slot 참조) 보존
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ReorderEquipmentsAsync(IReadOnlyList<int> ids)
    {
        var list = await _db.ScheduleEquipments.ToListAsync();
        for (int i = 0; i < ids.Count; i++)
        {
            var e = list.FirstOrDefault(x => x.Id == ids[i]);
            if (e is not null) e.OrderIndex = i;
        }
        await _db.SaveChangesAsync();
    }

    private static string Display(string text, int? temp, int s2) =>
        temp.HasValue ? $"{text.Split('@')[0]} (S2 {temp}℃ {s2}분)" : text.Split('@')[0];

    private static ScheduleBlockDto ToDto(ScheduleBlock b) => new(
        b.Id, b.BoardDate, b.EquipmentIndex, b.StartMinute,
        b.S2Minutes, b.HFMinutes, b.DIMinutes, b.S2Temperature, b.RecipeText);

    public async Task<IReadOnlyList<ScheduleBlockDto>> GetDayAsync(string boardDate)
    {
        var list = await _db.ScheduleBlocks.Where(b => b.BoardDate == boardDate)
            .OrderBy(b => b.EquipmentIndex).ThenBy(b => b.StartMinute).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>해당 날짜의 블록을 통째로 교체 저장 (WPF delete-all + insert-all).</summary>
    public async Task<IReadOnlyList<ScheduleBlockDto>> SaveDayAsync(
        string boardDate, IReadOnlyList<ScheduleBlockRow> blocks, IReadOnlyCollection<int>? knownIds = null)
    {
        var existing = await _db.ScheduleBlocks.Where(b => b.BoardDate == boardDate).ToListAsync();
        // 하루치를 통째로 바꾸는 저장이라, 두 사람이 같은 날을 열어 두면 나중에 저장한 사람 화면만 남았다.
        // 화면이 알고 있던 블록 묶음과 지금 DB 의 묶음이 다르면 덮어쓰지 않고 알린다.
        if (knownIds is not null && !existing.Select(b => b.Id).ToHashSet().SetEquals(knownIds))
            throw new CleanPotal.Core.ConcurrencyConflictException(
                "이 날짜 스케줄을 그 사이 다른 사용자가 먼저 바꿨습니다.");
        _db.ScheduleBlocks.RemoveRange(existing);

        var now = DateTime.Now;
        var added = blocks.Select(r => new ScheduleBlock
        {
            BoardDate = boardDate,
            EquipmentIndex = r.EquipmentIndex,
            StartMinute = r.StartMinute,
            S2Minutes = r.S2Minutes,
            HFMinutes = r.HFMinutes,
            DIMinutes = r.DIMinutes,
            S2Temperature = r.S2Temperature,
            RecipeText = r.RecipeText,
            CreatedTime = now,
        }).ToList();
        _db.ScheduleBlocks.AddRange(added);
        await _db.SaveChangesAsync();
        return added.Select(ToDto).ToList();
    }

    // ── 레시피 ──

    private static ScheduleRecipeDto RecipeDto(ScheduleRecipe r) => new(
        r.Id, r.Text, r.S2Minutes, r.HFMinutes, r.DIMinutes, r.S2Temperature, r.IsFavorite, r.OrderIndex,
        Display(r.Text, r.S2Temperature, r.S2Minutes));

    public async Task<IReadOnlyList<ScheduleRecipeDto>> GetRecipesAsync()
    {
        var list = await _db.ScheduleRecipes
            .OrderByDescending(r => r.IsFavorite).ThenBy(r => r.OrderIndex).ThenBy(r => r.Text).ToListAsync();
        return list.Select(RecipeDto).ToList();
    }

    /// <summary>레시피 파싱 (WPF RecipeDefinition.TryParse). "30-10-100" 또는 "120-30-100@60".</summary>
    private static bool TryParse(string input, out int s2, out int hf, out int di, out int? temp, out string text, out string msg)
    {
        s2 = hf = di = 0; temp = null; text = ""; msg = "";
        var n = (input ?? "").Trim().Replace(" ", "");
        if (n.Length == 0) { msg = "레시피 형식 오류"; return false; }
        var at = n.Split('@');
        if (at.Length == 2 && int.TryParse(at[1], out var t)) temp = t;
        var parts = at[0].Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[0], out s2) || !int.TryParse(parts[1], out hf) || !int.TryParse(parts[2], out di))
        { msg = "레시피 형식 오류 (예: 30-30-100)"; return false; }
        if (s2 < 0 || hf < 0 || di < 0) { msg = "음수는 입력할 수 없습니다."; return false; }
        text = temp.HasValue ? $"{s2}-{hf}-{di}@{temp}" : $"{s2}-{hf}-{di}";
        return true;
    }

    private async Task ReorderAsync()
    {
        var all = await _db.ScheduleRecipes.ToListAsync();
        int idx = 0;
        foreach (var r in all.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Text)) r.OrderIndex = idx++;
    }

    public async Task<(bool ok, string message, ScheduleRecipeDto? recipe)> AddRecipeAsync(string text)
    {
        if (!TryParse(text, out var s2, out var hf, out var di, out var temp, out var norm, out var msg))
            return (false, msg, null);
        var dup = await _db.ScheduleRecipes.FirstOrDefaultAsync(r =>
            r.S2Minutes == s2 && r.HFMinutes == hf && r.DIMinutes == di && r.S2Temperature == temp);
        if (dup is not null) return (false, $"이미 존재하는 레시피입니다: {norm}", RecipeDto(dup));

        var e = new ScheduleRecipe { Text = norm, S2Minutes = s2, HFMinutes = hf, DIMinutes = di, S2Temperature = temp };
        _db.ScheduleRecipes.Add(e);
        await _db.SaveChangesAsync();
        await ReorderAsync();
        await _db.SaveChangesAsync();
        return (true, $"레시피 추가 완료: {norm}", RecipeDto(e));
    }

    public async Task<(bool ok, string message, ScheduleRecipeDto? recipe)> UpdateRecipeAsync(int id, int s2, int hf, int di, int? temp)
    {
        var r = await _db.ScheduleRecipes.FindAsync(id);
        if (r is null) return (false, "레시피를 찾을 수 없습니다.", null);
        if (s2 < 0 || hf < 0 || di < 0) return (false, "음수는 입력할 수 없습니다.", null);
        var norm = temp.HasValue ? $"{s2}-{hf}-{di}@{temp}" : $"{s2}-{hf}-{di}";
        var dup = await _db.ScheduleRecipes.FirstOrDefaultAsync(x =>
            x.Id != id && x.S2Minutes == s2 && x.HFMinutes == hf && x.DIMinutes == di && x.S2Temperature == temp);
        if (dup is not null) return (false, $"이미 존재하는 레시피입니다: {norm}", null);
        r.S2Minutes = s2; r.HFMinutes = hf; r.DIMinutes = di; r.S2Temperature = temp; r.Text = norm;
        await _db.SaveChangesAsync();
        await ReorderAsync();
        await _db.SaveChangesAsync();
        return (true, $"레시피 수정 완료: {norm}", RecipeDto(r));
    }

    public async Task<bool> DeleteRecipeAsync(int id)
    {
        var r = await _db.ScheduleRecipes.FindAsync(id);
        if (r is null) return false;
        _db.ScheduleRecipes.Remove(r);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ScheduleRecipeDto?> SetFavoriteAsync(int id, bool favorite)
    {
        var r = await _db.ScheduleRecipes.FindAsync(id);
        if (r is null) return null;
        r.IsFavorite = favorite;
        await _db.SaveChangesAsync();
        await ReorderAsync();
        await _db.SaveChangesAsync();
        return RecipeDto(r);
    }
}
