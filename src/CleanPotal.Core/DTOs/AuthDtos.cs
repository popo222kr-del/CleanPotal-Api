namespace CleanPotal.Core.DTOs;

public record LoginRequest(string Username, string Password);

/// <summary>본인 아이디/비밀번호 변경. NewUsername·NewPassword는 비우면 해당 항목 유지.</summary>
public record ChangeCredentialsRequest(string CurrentPassword, string? NewUsername, string? NewPassword);

public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    UserDto User
);

/// <summary>외부로 노출하는 사용자 정보 (PasswordHash 제외).</summary>
public record UserDto(
    int Id,
    string Username,
    string RealName,
    string TeamName,
    string JobTitle,
    string Email,
    string PhoneNumber,
    string EmployeeNumber,
    string HireDate,
    bool IsResigned,
    string ResignDate,
    bool IsAdmin,
    bool CanManageFiles,
    bool CanManageNotices,
    bool CanManageVendors,
    bool CanManageSchedule,
    bool CanManageBroken,
    bool CanAccessEtcMenu,
    bool CanManageShiftBoard,
    bool CanManageInventory
);

/// <summary>사용자 생성/수정 요청.</summary>
public record UserUpsertRequest(
    string Username,
    string? Password,          // 생성 시 필수, 수정 시 비우면 유지
    string RealName,
    string TeamName,
    string JobTitle,
    string Email,
    string PhoneNumber,
    string EmployeeNumber,
    string HireDate,
    bool IsResigned,
    string ResignDate,
    bool IsAdmin,              // 관리자(전체 권한) — 1004는 항상 유지
    bool CanManageFiles,
    bool CanManageNotices,
    bool CanManageVendors,
    bool CanManageSchedule,
    bool CanManageBroken,
    bool CanAccessEtcMenu,
    bool CanManageShiftBoard,
    bool CanManageInventory
);

/// <summary>권한 매트릭스 일괄 변경. Key = isAdmin | files | notices | vendors | schedule | broken | etc | shiftboard | inventory.</summary>
public record UserPermChange(int Id, string Key, bool Value);
public record UserPermBulkRequest(List<UserPermChange> Changes);

public record UserAuditDto(int Id, string TargetUser, string Action, string Detail, string ByUser, string CreatedAt);
