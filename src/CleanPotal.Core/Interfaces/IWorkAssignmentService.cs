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

    /// <summary>기본 교육 기록 표를 통째로 저장한다(빠진 줄은 삭제).</summary>
    Task<IReadOnlyList<WorkEduDto>> SaveEdusAsync(WorkEduBulkSaveRequest req);

    /// <summary>다른 사람의 교육 목록을 가져온다(교육명만).</summary>
    Task<IReadOnlyList<WorkEduDto>> CopyEdusAsync(WorkEduCopyRequest req);
}
