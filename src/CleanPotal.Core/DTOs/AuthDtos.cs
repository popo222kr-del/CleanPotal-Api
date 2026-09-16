namespace CleanPotal.Core.DTOs;

public record LoginRequest(string Username, string Password);

/// <summary>본인 아이디/비밀번호 변경. NewUsername·NewPassword는 비우면 해당 항목 유지.</summary>
public record ChangeCredentialsRequest(string CurrentPassword, string? NewUsername, string? NewPassword);

public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    UserDto User
);

/// <summary>외부로 노출하는 사용자 정보 (PasswordHash 제외). Access* = 0 없음 / 1 조회 / 2 편집.</summary>
public record UserDto(
    int Id,
    string Username,
    string RealName,
    string Department,
    string TeamName,
    string Rank,               // 직급(호칭) — 사원·주임·대리…
    string JobTitle,           // 직위(맡은 일) — QA팀장·세정팀장…
    string Email,
    string PhoneNumber,
    string EmployeeNumber,
    string HireDate,
    string Tenure,             // 입사일로 계산한 근속("8년 3개월"). 해석 불가면 빈 문자열
    bool IsResigned,
    string ResignDate,
    bool IsAdmin,
    int AccessSchedule,
    int AccessRoster,
    int AccessHandover,
    int AccessField,
    int AccessOffice,
    int AccessMes,
    string HiddenMenus          // 숨긴 하위 메뉴 경로 JSON 배열 (예: ["/meeting"])
);

/// <summary>사용자 생성/수정 요청.</summary>
public record UserUpsertRequest(
    string Username,
    string? Password,          // 생성 시 필수, 수정 시 비우면 유지
    string RealName,
    string Department,
    string TeamName,
    string Rank,               // 직급(호칭)
    string JobTitle,           // 직위(맡은 일)
    string Email,
    string PhoneNumber,
    string EmployeeNumber,
    string HireDate,
    bool IsResigned,
    string ResignDate,
    bool IsAdmin,              // 관리자(전체 권한) — 1004는 항상 유지
    int AccessSchedule,
    int AccessRoster,
    int AccessHandover,
    int AccessField,
    int AccessOffice,
    int AccessMes,
    string? HiddenMenus         // 숨긴 하위 메뉴 경로 JSON 배열
);

/// <summary>권한 매트릭스 일괄 변경. Key = isAdmin | schedule | roster | handover | field | office | mes.
/// Value: isAdmin은 0/1, 나머지는 0(없음)/1(조회)/2(편집).</summary>
public record UserPermChange(int Id, string Key, int Value);
public record UserPermBulkRequest(List<UserPermChange> Changes);

/// <summary>팀 단위 일괄 변경: 팀명 변경(전원) 및/또는 부서 지정.
/// <c>Department</c>: 대상 팀이 속한 현재 부서. Office 처럼 같은 이름 팀이 여러 부서에 있을 수 있어
/// 이 값을 주면 그 부서의 팀만 바꾼다. 비우면(null) 예전처럼 같은 이름 팀 전체가 대상이다.</summary>
public record TeamBulkRequest(string Team, string? NewTeam, string? NewDepartment, string? Department = null);

/// <summary>부서명 일괄 변경: 해당 부서 전원의 부서명을 바꾼다.</summary>
public record DeptBulkRequest(string OldDept, string NewDept);

// ── 조직도(부서·팀) ──
public record OrgMemberDto(int Id, string RealName, string Rank, string JobTitle);
/// <summary><c>ShiftGroup</c>: 0 = 교대 없음, 1 = 1조, 2 = 2조(1조와 반대 근무).
/// 근무 예측은 팀 이름이 아니라 이 값을 본다.</summary>
public record OrgTeamDto(string Name, bool Registered, IReadOnlyList<OrgMemberDto> Members, int ShiftGroup,
                         string LegacyNames,
                         // 생산팀 여부. 교대조가 지정된 팀은 정의상 생산팀이라 항상 true 로 내려간다.
                         bool IsProduction);
/// <summary><c>Color</c>·<c>ShortName</c> 은 달력 표시용으로 서버가 정한 값(자동 배정 포함).
/// <c>Division</c>: 소속 본부(사업본부). 지정하지 않았으면 빈 문자열.</summary>
public record OrgDeptDto(string Name, bool Registered, IReadOnlyList<OrgTeamDto> Teams,
                         int Id, string Color, string ShortName, string Division);

/// <summary>조직도 전체. 본부 > 부서 > 팀 > 인원의 3단 구조.
/// <c>Divisions</c> 에는 소속 부서가 아직 없는 본부도 들어간다(미리 만들어 둘 수 있으므로).</summary>
public record OrgTreeDto(IReadOnlyList<string> Divisions, IReadOnlyList<OrgDeptDto> Depts);

/// <summary>본부/부서/팀 추가·삭제 요청. Kind = division | dept | team. team이면 Parent에 부서명.</summary>
public record OrgUnitRequest(string Kind, string Name, string? Parent);

/// <summary>부서를 본부에 연결한다. Division 을 비우면 '본부 미지정' 으로 돌린다.</summary>
public record OrgDeptDivisionRequest(string Dept, string Division);

/// <summary>본부 이름 변경. 그 본부를 가리키는 부서들도 함께 따라간다.</summary>
public record OrgDivisionRenameRequest(string OldName, string NewName);

/// <summary>팀의 교대 조 지정. 0 = 교대 없음, 1 = 1조, 2 = 2조(1조와 반대 근무).</summary>
public record OrgShiftGroupRequest(string Name, int ShiftGroup, string? Parent = null);

/// <summary>팀의 생산팀 여부. 근무표 표시와 생산/사무 집계를 가른다(교대조와 별개).</summary>
public record OrgProductionRequest(string Name, bool IsProduction, string? Parent = null);

/// <summary>이 팀이 WPF 에서 쓰던 이름들(쉼표 구분). 임포트할 때 현재 이름으로 바꿔 넣는다.</summary>
public record OrgLegacyNamesRequest(string Name, string LegacyNames, string? Parent = null);

/// <summary>부서 표시 설정 — 달력에서 쓸 색(#RRGGBB)과 약칭. 비우면 자동값을 쓴다.</summary>
public record OrgDeptStyleRequest(string Name, string? Color, string? ShortName);

public record UserAuditDto(int Id, string TargetUser, string Action, string Detail, string ByUser, string CreatedAt);
