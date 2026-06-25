using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IProdReqService
{
    Task<IReadOnlyList<ProdReqDto>> GetAllAsync(string? status, string? search);
    Task<ProdReqDto> CreateAsync(ProdReqUpsertRequest req, string requester);
    Task<ProdReqDto?> UpdateAsync(int id, ProdReqUpsertRequest req);
    Task<ProdReqDto?> ChangeStatusAsync(int id, string status);
    Task<bool> DeleteAsync(int id);
}
