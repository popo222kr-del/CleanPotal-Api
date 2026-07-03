using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>개인별 업무 분장표 — 인원·계정·교육이수.</summary>
public interface IWorkAssignmentService
{
    Task<IReadOnlyList<WorkMemberDto>> GetMembersAsync(bool includeHidden);
    Task<WorkMemberDetailDto?> GetMemberAsync(string username);
    Task<WorkMemberDto> AddMemberAsync(WorkMemberUpsertRequest req);
    Task<WorkMemberDto?> UpdateMemberAsync(int id, WorkMemberUpsertRequest req);
    Task<bool> DeleteMemberAsync(int id);

    Task<WorkAccountDto> SaveAccountAsync(WorkAccountUpsertRequest req);
    Task<WorkAccountDto?> UpdateAccountAsync(int id, WorkAccountUpsertRequest req);
    Task<bool> DeleteAccountAsync(int id);

    Task<WorkEduDto> SaveEduAsync(WorkEduUpsertRequest req);
    Task<WorkEduDto?> UpdateEduAsync(int id, WorkEduUpsertRequest req);
    Task<bool> DeleteEduAsync(int id);
}
