using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class RecipeService : IRecipeService
{
    private readonly CleanPotalDbContext _db;
    public RecipeService(CleanPotalDbContext db) => _db = db;

    private static RecipeDto ToDto(Recipe r) =>
        new(r.Id, r.Text, r.DisplayText, r.S2Minutes, r.S2Temperature, r.HfMinutes, r.DiMinutes,
            r.TotalMinutes, r.IsFavorite, r.OrderIndex);

    public async Task<IReadOnlyList<RecipeDto>> GetAllAsync(string? search)
    {
        var q = _db.Recipes.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(r => r.Text.Contains(search) || r.DisplayText.Contains(search));
        // 즐겨찾기 먼저, 그다음 지정 순서
        var list = await q.OrderByDescending(r => r.IsFavorite).ThenBy(r => r.OrderIndex).ThenBy(r => r.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<RecipeDto> CreateAsync(RecipeUpsertRequest req)
    {
        var r = new Recipe();
        Apply(r, req);
        _db.Recipes.Add(r);
        await _db.SaveChangesAsync();
        return ToDto(r);
    }

    public async Task<RecipeDto?> UpdateAsync(int id, RecipeUpsertRequest req)
    {
        var r = await _db.Recipes.FindAsync(id);
        if (r is null) return null;
        Apply(r, req);
        await _db.SaveChangesAsync();
        return ToDto(r);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var r = await _db.Recipes.FindAsync(id);
        if (r is null) return false;
        _db.Recipes.Remove(r);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void Apply(Recipe r, RecipeUpsertRequest req)
    {
        r.Text = req.Text;
        r.DisplayText = req.DisplayText;
        r.S2Minutes = req.S2Minutes;
        r.S2Temperature = req.S2Temperature;
        r.HfMinutes = req.HfMinutes;
        r.DiMinutes = req.DiMinutes;
        r.TotalMinutes = req.TotalMinutes;
        r.IsFavorite = req.IsFavorite;
        r.OrderIndex = req.OrderIndex;
    }
}
