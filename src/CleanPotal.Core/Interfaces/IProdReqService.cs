using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IProdReqService
{
    Task<IReadOnlyList<ProdReqDto>> GetAllAsync(string? status, string? search);
    Task<ProdReqDto> CreateAsync(ProdReqUpsertRequest req, string requester);
    Task<ProdReqDto?> UpdateAsync(int id, ProdReqUpsertRequest req, string actor);
    Task<ProdReqDto?> ChangeStatusAsync(int id, string status);
    Task<bool> DeleteAsync(int id);

    // ── 등록 옵션 (구분/세부 위치/요청 분류) ──
    Task<ProdReqOptionsDto> GetOptionsAsync();
    Task<ProdReqOptionsDto> SaveOptionsAsync(ProdReqOptionsDto dto);

    // ── 미확인(새 요청) 뱃지 — WPF ProdReqReadState ──
    Task<int> GetUnreadCountAsync(string username);
    Task MarkReadAsync(string username);
}
