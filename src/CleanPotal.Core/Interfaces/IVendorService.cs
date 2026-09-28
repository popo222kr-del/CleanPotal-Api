using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IVendorService
{
    /// <summary>업체 목록. 관리자가 아니면 본인 부서 업체만. <paramref name="mineOnly"/> 면 관리자도 본인 부서만
    /// (기타세정 현황·배차표처럼 한 부서 업무 화면에서 고르는 목록).</summary>
    Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search, bool mineOnly = false);
    Task<VendorDto> CreateAsync(VendorUpsertRequest req);
    Task<VendorDto?> UpdateAsync(int id, VendorUpsertRequest req);
    Task<bool> DeleteAsync(int id);

    /// <summary>즐겨찾기만 토글 (다른 필드 미변경). 없으면 null.</summary>
    Task<bool?> ToggleFavoriteAsync(int id);
}
