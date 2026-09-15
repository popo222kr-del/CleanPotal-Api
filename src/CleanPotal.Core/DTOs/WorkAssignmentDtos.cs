namespace CleanPotal.Core.DTOs;

/// <summary>
/// 분장표 인원 한 명. 표시용 정보(이름·부서·팀·직위·사번·재직여부)는 계정(Users)에서 가져온다.
/// <c>Username</c> 은 WPF 에서 넘어온 연결 키로, 계정·교육이수가 이 값으로 묶여 있어 바꾸지 않는다.
/// </summary>
public record WorkMemberDto(
    int Id, string Username, string RealName, string Department, string TeamName, string JobTitle,
    string EmployeeNumber,
    string HireDate,
    string Tenure,          // 입사일로 계산한 경력 ("8년 3개월"). 해석 못 하면 빈 문자열
    string Email,
    string PhoneNumber,
    bool IsResigned,        // 계정(User.IsResigned) 기준 — 사용자 계정 관리 화면과 같은 값
    string ResignDate,
    bool IsHidden,
    bool HasAccount,        // false = 연결된 계정을 찾지 못함(이름 대신 사번이 뜨던 경우)
    int? LinkedUserId,      // 연결된 계정 PK. 같은 값이 둘 이상이면 한 사람이 두 번 등록된 것
    int AccountCount,       // 이 행에 붙어 있는 계정 수 — 중복 행 중 어느 쪽이 비었는지 판단용
    int EduCount);

public record WorkAccountDto(int Id, string Username, string ServiceName, string AccountId, string AccountPassword, string Note);

/// <summary>
/// 교육 이수 한 건. <c>EduDateText</c> 는 시작·종료일을 WPF 와 같은 한 줄로 합친 표시용 값이다
/// (예: <c>2018-06-04</c>, <c>2018-06-04~07</c>). 편집은 <c>StartDate</c>/<c>EndDate</c> 로 한다.
/// </summary>
public record WorkEduDto(int Id, string Username, string EduName, string EduDate, string Instructor, string Note,
                         string StartDate, string EndDate, string EduDateText);

/// <summary>
/// 인원 상세 — 계정 + 기본 교육 기록 + 외부 교육 기록.
///
/// <c>ExternalEdus</c> 는 교육 현황 대시보드(EducationPlans)에서 자동으로 끌어온다.
/// 여기서 편집하지 않는다(대시보드가 정본).
/// </summary>
public record WorkMemberDetailDto(
    WorkMemberDto Member,
    IReadOnlyList<WorkAccountDto> Accounts,
    IReadOnlyList<WorkEduDto> Edus,
    IReadOnlyList<EducationPlanDto> ExternalEdus,
    // 교육 현황 대시보드는 사람을 '실명 문자열'로 기록한다. 같은 이름이 둘 이상이면
    // 남의 교육 기록이 섞여 보일 수 있어, 숨기지 말고 화면에서 알리도록 표시해 둔다.
    bool ExternalEduNameAmbiguous);

public record WorkMemberUpsertRequest(string Username, bool IsHidden, string? ResignDate);
public record WorkAccountUpsertRequest(string Username, string ServiceName, string AccountId, string? AccountPassword, string? Note);
public record WorkEduUpsertRequest(string Username, string EduName, string? EduDate, string? Instructor, string? Note, string? StartDate, string? EndDate);
