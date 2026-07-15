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

    // ── 설비 (DB 마스터) — Index=Slot(블록 참조), OrderIndex=표시순서 ──
    private static ScheduleEquipmentDto EquipDto(ScheduleEquipment e) =>
        new(e.Slot, e.Name, e.Id, e.GroupName, e.OrderIndex);

    public async Task<IReadOnlyList<ScheduleEquipmentDto>> GetEquipmentsAsync()
    {
        var list = await _db.ScheduleEquipments.Where(e => e.IsActive)
            .OrderBy(e => e.OrderIndex).ThenBy(e => e.Id).ToListAsync();
        return list.Select(EquipDto).ToList();
    }

    public async Task<ScheduleEquipmentDto> AddEquipmentAsync(string name, string groupName)
    {
        var maxSlot = await _db.ScheduleEquipments.Select(e => (int?)e.Slot).MaxAsync() ?? -1;
        var maxOrder = await _db.ScheduleEquipments.Where(e => e.IsActive).Select(e => (int?)e.OrderIndex).MaxAsync() ?? -1;
        var e = new ScheduleEquipment
        {
            Name = (name ?? "").Trim(),
            GroupName = string.IsNullOrWhiteSpace(groupName) ? "MDC" : groupName.Trim(),
            Slot = maxSlot + 1, OrderIndex = maxOrder + 1, IsActive = true,
        };
        _db.ScheduleEquipments.Add(e);
        await _db.SaveChangesAsync();
        return EquipDto(e);
    }

    public async Task<ScheduleEquipmentDto?> UpdateEquipmentAsync(int id, string name, string groupName)
    {
        var e = await _db.ScheduleEquipments.FindAsync(id);
        if (e is null) return null;
        if (!string.IsNullOrWhiteSpace(name)) e.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(groupName)) e.GroupName = groupName.Trim();
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
    public async Task<IReadOnlyList<ScheduleBlockDto>> SaveDayAsync(string boardDate, IReadOnlyList<ScheduleBlockRow> blocks)
    {
        var existing = await _db.ScheduleBlocks.Where(b => b.BoardDate == boardDate).ToListAsync();
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
