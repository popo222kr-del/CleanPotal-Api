using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>교육 현황 대시보드. 등록/수정 시 근무 스케줄러에 "교육" 자동 연동.</summary>
public interface IEducationService
{
    Task<IReadOnlyList<EducationPlanDto>> GetAllAsync(int? year, string? status, string? search);
    Task<EducationPlanDto> CreateAsync(EducationUpsertRequest req);
    Task<EducationPlanDto?> UpdateAsync(int id, EducationUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
