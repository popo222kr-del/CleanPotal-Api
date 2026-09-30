using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IHandoverService
{
    Task<IReadOnlyList<HandoverDto>> GetAllAsync(string? status, string? category, string? search, bool weekly, string actor = "");
    Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync(bool weekly);
    Task<HandoverDto> CreateAsync(HandoverUpsertRequest req, string actor);
    Task<HandoverDto?> UpdateAsync(int id, HandoverUpsertRequest req, string actor, bool isAdmin);
    Task<HandoverDto?> ChangeStatusAsync(int id, string status, string actor, bool isAdmin);
    Task<bool> MarkReadAsync(int id, string actor);
    Task<bool> DeleteAsync(int id, bool isAdmin);
    /// <summary>이 업체(또는 이 항목의 업체)가 주간세정 현황 쪽인지 — 메뉴 '조회만'(기타세정/주간세정) 판정용. 항목이 없으면 null.</summary>
    Task<bool> IsWeeklyVendorAsync(string vendor);
    Task<bool?> IsWeeklyItemAsync(int id);
}
