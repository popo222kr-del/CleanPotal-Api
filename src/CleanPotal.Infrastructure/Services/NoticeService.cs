using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 사무실 공지.
/// 공지는 <b>작성자 책임이 중요한 항목</b>이라 수정·삭제 모두 작성자 본인 또는 관리자만 가능하다.
/// (인수인계처럼 여러 사람이 이어서 채우는 공동 업무와 다르게 취급한다)
/// </summary>
public class NoticeService : INoticeService
{
    private const string What = "공지";

    private readonly CleanPotalDbContext _db;
    private readonly ICurrentUser _me;
    public NoticeService(CleanPotalDbContext db, ICurrentUser me) { _db = db; _me = me; }

    private NoticeDto ToDto(Notice n) => new(
        n.Id, n.Title, n.Content, n.Author, n.CreatedAt,
        n.RowVersion,
        ContentOwnership.IsOwnerOrAdmin(_me, n.CreatorUserId, n.Author));

    public async Task<IReadOnlyList<NoticeDto>> GetAllAsync()
    {
        var list = await _db.Notices.OrderByDescending(n => n.CreatedAt).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<NoticeDto> CreateAsync(NoticeUpsertRequest req, string author)
    {
        var n = new Notice
        {
            Title = req.Title,
            Content = req.Content,
            Author = author,
            CreatorUserId = _me.Id,        // 작성자는 이름이 아니라 계정 ID 로 기록
            CreatedAt = DateTime.Now,
        };
        _db.Notices.Add(n);
        await _db.SaveChangesAsync();       // Id 확정 후 이력을 남긴다
        ContentAuditWriter.Add(_db, _me, What, n.Id, "생성", n.Title);
        await _db.SaveChangesAsync();
        return ToDto(n);
    }

    public async Task<NoticeDto?> UpdateAsync(int id, NoticeUpsertRequest req)
    {
        var n = await _db.Notices.FindAsync(id);
        if (n is null) return null;
        ContentOwnership.EnsureOwnerOrAdmin(_me, n.CreatorUserId, n.Author, What, "수정");
        ContentAuditWriter.EnsureNotStale(req.RowVersion, n.RowVersion, What);

        var detail = ContentAuditWriter.Describe(
            ("제목", n.Title, req.Title),
            ("내용", n.Content, req.Content));

        n.Title = req.Title;
        n.Content = req.Content;
        n.RowVersion++;
        ContentAuditWriter.Add(_db, _me, What, n.Id, "수정", detail);
        await ContentAuditWriter.SaveAsync(_db, What);
        return ToDto(n);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var n = await _db.Notices.FindAsync(id);
        if (n is null) return false;
        ContentOwnership.EnsureOwnerOrAdmin(_me, n.CreatorUserId, n.Author, What, "삭제");

        ContentAuditWriter.Add(_db, _me, What, n.Id, "삭제", n.Title);
        _db.Notices.Remove(n);
        await ContentAuditWriter.SaveAsync(_db, What);
        return true;
    }
}
