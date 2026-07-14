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

    // WPF SeedEquipments 와 동일 (고정 19대)
    private static readonly string[] Equipments =
    {
        "MDC01 (POLY)", "MDC02 (Hot Chemical)", "MDC03 (Hot Chemical)", "MDC04 (POLY)", "MDC05 (TEOS)",
        "MDC06 (ALO/HFO)", "MDC07 (POLY)", "MDC08 (N,G,D-POLY)", "MDC09 (SIGE)", "MDC10 (ALO/HFO)",
        "MSC01-1 (POLY/대대배치)", "MSC01-2 (Rinse 전용)",
        "NDC01 (WOOAM)", "NDC02 (OXIDE)", "NDC03 (A급)", "NDC04 (A급)", "NDC05 (N,G,D-POLY)",
        "NDC06 (Hot Chemical)", "NDC07 (SiN)"
    };

    public IReadOnlyList<ScheduleEquipmentDto> GetEquipments()
        => Equipments.Select((n, i) => new ScheduleEquipmentDto(i, n)).ToList();

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
