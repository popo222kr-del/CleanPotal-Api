using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class PortalService : IPortalService
{
    private readonly CleanPotalDbContext _db;
    public PortalService(CleanPotalDbContext db) => _db = db;

    /// <summary>경로/URL로 유형 자동 판별 (Flask auto_detect_type 이식).</summary>
    private static string DetectType(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "folder";
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "web";
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".xls" or ".xlsx" or ".csv" => "excel",
            ".ppt" or ".pptx" => "ppt",
            ".pdf" => "pdf",
            _ => "folder",
        };
    }

    private static PortalItemDto ItemDto(PortalItem i) => new(i.Id, i.GroupId, i.Title, i.Path, i.Type, i.SortOrder);

    public async Task<IReadOnlyList<PortalGroupDto>> GetAllAsync()
    {
        var groups = await _db.PortalGroups
            .Include(g => g.Items)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync();
        return groups.Select(g => new PortalGroupDto(
            g.Id, g.Name, g.SortOrder,
            g.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Title).Select(ItemDto).ToList()
        )).ToList();
    }

    public async Task<PortalGroupDto> AddGroupAsync(PortalGroupRequest req)
    {
        int maxOrder = await _db.PortalGroups.AnyAsync() ? await _db.PortalGroups.MaxAsync(g => g.SortOrder) : 0;
        var g = new PortalGroup { Name = req.Name, SortOrder = maxOrder + 1 };
        _db.PortalGroups.Add(g);
        await _db.SaveChangesAsync();
        return new PortalGroupDto(g.Id, g.Name, g.SortOrder, Array.Empty<PortalItemDto>());
    }

    public async Task<PortalGroupDto?> RenameGroupAsync(int id, PortalGroupRequest req)
    {
        var g = await _db.PortalGroups.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (g is null) return null;
        g.Name = req.Name;
        await _db.SaveChangesAsync();
        return new PortalGroupDto(g.Id, g.Name, g.SortOrder, g.Items.Select(ItemDto).ToList());
    }

    public async Task<bool> DeleteGroupAsync(int id)
    {
        var g = await _db.PortalGroups.FindAsync(id);
        if (g is null) return false;
        _db.PortalGroups.Remove(g);   // Items는 Cascade 삭제
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<PortalItemDto> AddItemAsync(PortalItemRequest req)
    {
        int maxOrder = await _db.PortalItems.Where(i => i.GroupId == req.GroupId).AnyAsync()
            ? await _db.PortalItems.Where(i => i.GroupId == req.GroupId).MaxAsync(i => i.SortOrder) : 0;
        var item = new PortalItem
        {
            GroupId = req.GroupId,
            Title = req.Title,
            Path = req.Path,
            Type = string.IsNullOrWhiteSpace(req.Type) ? DetectType(req.Path) : req.Type!,
            SortOrder = maxOrder + 1,
        };
        _db.PortalItems.Add(item);
        await _db.SaveChangesAsync();
        return ItemDto(item);
    }

    public async Task<PortalItemDto?> UpdateItemAsync(int id, PortalItemRequest req)
    {
        var item = await _db.PortalItems.FindAsync(id);
        if (item is null) return null;
        item.Title = req.Title;
        item.Path = req.Path;
        item.Type = string.IsNullOrWhiteSpace(req.Type) ? DetectType(req.Path) : req.Type!;
        await _db.SaveChangesAsync();
        return ItemDto(item);
    }

    public async Task<bool> DeleteItemAsync(int id)
    {
        var item = await _db.PortalItems.FindAsync(id);
        if (item is null) return false;
        _db.PortalItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }
}
