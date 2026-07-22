using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IVendorService
{
    Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search);
    Task<VendorDto> CreateAsync(VendorUpsertRequest req);
    Task<VendorDto?> UpdateAsync(int id, VendorUpsertRequest req);
    Task<bool> DeleteAsync(int id);

    /// <summary>즐겨찾기만 토글 (다른 필드 미변경). 없으면 null.</summary>
    Task<bool?> ToggleFavoriteAsync(int id);
}
