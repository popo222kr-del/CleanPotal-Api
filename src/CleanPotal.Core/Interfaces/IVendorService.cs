using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IVendorService
{
    Task<IReadOnlyList<VendorDto>> GetAllAsync(string? search);
    Task<VendorDto> CreateAsync(VendorUpsertRequest req);
    Task<VendorDto?> UpdateAsync(int id, VendorUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
