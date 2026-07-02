namespace CleanPotal.Core.DTOs;

public record LoginRequest(string Username, string Password);

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
    bool CanManageFiles,
    bool CanManageNotices,
    bool CanManageVendors,
    bool CanManageSchedule,
    bool CanManageBroken,
    bool CanAccessEtcMenu,
    bool CanManageShiftBoard,
    bool CanManageInventory
);
