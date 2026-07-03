using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class NoticeService : INoticeService
{
    private readonly CleanPotalDbContext _db;
    public NoticeService(CleanPotalDbContext db) => _db = db;

    private static NoticeDto ToDto(Notice n) => new(n.Id, n.Title, n.Content, n.Author, n.CreatedAt);

    public async Task<IReadOnlyList<NoticeDto>> GetAllAsync()
    {
        var list = await _db.Notices.OrderByDescending(n => n.CreatedAt).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<NoticeDto> CreateAsync(NoticeUpsertRequest req, string author)
    {
        var n = new Notice { Title = req.Title, Content = req.Content, Author = author, CreatedAt = DateTime.Now };
        _db.Notices.Add(n);
        await _db.SaveChangesAsync();
        return ToDto(n);
    }

    public async Task<NoticeDto?> UpdateAsync(int id, NoticeUpsertRequest req)
    {
        var n = await _db.Notices.FindAsync(id);
        if (n is null) return null;
        n.Title = req.Title;
        n.Content = req.Content;
        await _db.SaveChangesAsync();
        return ToDto(n);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var n = await _db.Notices.FindAsync(id);
        if (n is null) return false;
        _db.Notices.Remove(n);
        await _db.SaveChangesAsync();
        return true;
    }
}
