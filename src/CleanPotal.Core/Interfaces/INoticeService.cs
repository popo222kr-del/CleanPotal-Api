using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>사무실 공지 관리.</summary>
public interface INoticeService
{
    Task<IReadOnlyList<NoticeDto>> GetAllAsync();
    Task<NoticeDto> CreateAsync(NoticeUpsertRequest req, string author);
    Task<NoticeDto?> UpdateAsync(int id, NoticeUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
