namespace CleanPotal.Core.DTOs;

public record WorkMemberDto(
    int Id, string Username, string RealName, string TeamName, string JobTitle,
    bool IsHidden, string ResignDate);

public record WorkAccountDto(int Id, string Username, string ServiceName, string AccountId, string AccountPassword, string Note);

public record WorkEduDto(int Id, string Username, string EduName, string EduDate, string Instructor, string Note, string StartDate, string EndDate);

/// <summary>인원 상세 (계정 + 교육이수).</summary>
public record WorkMemberDetailDto(WorkMemberDto Member, IReadOnlyList<WorkAccountDto> Accounts, IReadOnlyList<WorkEduDto> Edus);

public record WorkMemberUpsertRequest(string Username, bool IsHidden, string? ResignDate);
public record WorkAccountUpsertRequest(string Username, string ServiceName, string AccountId, string? AccountPassword, string? Note);
public record WorkEduUpsertRequest(string Username, string EduName, string? EduDate, string? Instructor, string? Note, string? StartDate, string? EndDate);
