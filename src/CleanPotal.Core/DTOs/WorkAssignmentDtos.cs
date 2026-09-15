namespace CleanPotal.Core.DTOs;

public record WorkMemberDto(
    int Id, string Username, string RealName, string TeamName, string JobTitle,
    bool IsHidden, string ResignDate);

public record WorkAccountDto(int Id, string Username, string ServiceName, string AccountId, string AccountPassword, string Note);

/// <summary>
/// 교육 이수 한 건. <c>EduDateText</c> 는 시작·종료일을 WPF 와 같은 한 줄로 합친 표시용 값이다
/// (예: <c>2018-06-04</c>, <c>2018-06-04~07</c>). 편집은 <c>StartDate</c>/<c>EndDate</c> 로 한다.
/// </summary>
public record WorkEduDto(int Id, string Username, string EduName, string EduDate, string Instructor, string Note,
                         string StartDate, string EndDate, string EduDateText);

/// <summary>인원 상세 (계정 + 교육이수).</summary>
public record WorkMemberDetailDto(WorkMemberDto Member, IReadOnlyList<WorkAccountDto> Accounts, IReadOnlyList<WorkEduDto> Edus);

public record WorkMemberUpsertRequest(string Username, bool IsHidden, string? ResignDate);
public record WorkAccountUpsertRequest(string Username, string ServiceName, string AccountId, string? AccountPassword, string? Note);
public record WorkEduUpsertRequest(string Username, string EduName, string? EduDate, string? Instructor, string? Note, string? StartDate, string? EndDate);
