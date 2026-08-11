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
    string JobTitle,
    string Email,
    string PhoneNumber,
    string EmployeeNumber,
    string HireDate,
    bool IsResigned,
    string ResignDate,
    bool IsAdmin,
    int AccessSchedule,
    int AccessRoster,
    int AccessHandover,
    int AccessField,
    int AccessOffice,
    string HiddenMenus          // 숨긴 하위 메뉴 경로 JSON 배열 (예: ["/meeting"])
);

/// <summary>사용자 생성/수정 요청.</summary>
public record UserUpsertRequest(
    string Username,
    string? Password,          // 생성 시 필수, 수정 시 비우면 유지
    string RealName,
    string Department,
    string TeamName,
    string JobTitle,
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
    string? HiddenMenus         // 숨긴 하위 메뉴 경로 JSON 배열
);

/// <summary>권한 매트릭스 일괄 변경. Key = isAdmin | schedule | roster | handover | field | office.
/// Value: isAdmin은 0/1, 나머지는 0(없음)/1(조회)/2(편집).</summary>
public record UserPermChange(int Id, string Key, int Value);
public record UserPermBulkRequest(List<UserPermChange> Changes);

/// <summary>팀 단위 일괄 변경: 팀명 변경(전원) 및/또는 부서 지정.</summary>
public record TeamBulkRequest(string Team, string? NewTeam, string? NewDepartment);

public record UserAuditDto(int Id, string TargetUser, string Action, string Detail, string ByUser, string CreatedAt);
