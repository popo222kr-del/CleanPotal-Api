using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// 2026-08-28: 로그인 아이디 계정으로 전환. WindowsAccount 대신 LoginId/표시이름/활성여부를 노출한다.
// 2026-08-31 피드백(#5): 팀명(TeamName) 추가.
public record UserDto(int UserId, string LoginId, string? DisplayName, string? TeamName, UserRole Role, bool IsActive, DateTime UpdatedAt);

public record UserDetailDto(int UserId, string LoginId, string? DisplayName, string? TeamName, UserRole Role, bool IsActive, IReadOnlyList<PermissionCode> GrantedPermissions);

public record UpdateUserRoleRequest(int UserId, UserRole Role);

public record SetUserPermissionRequest(int UserId, PermissionCode PermissionCode, bool Granted);

// 계정 신규 생성(관리자). 초기 비밀번호는 최초 로그인 시 변경을 유도한다(MustChangePassword).
public record CreateUserAccountRequest(string LoginId, string? DisplayName, string? TeamName, string InitialPassword, UserRole Role);

// 2026-08-31 피드백(#5): 본인(또는 관리자) 계정 정보 수정 - 아이디/이름/팀명. 비밀번호는 별도(ResetPassword).
public record UpdateUserProfileRequest(int UserId, string LoginId, string? DisplayName, string? TeamName);

public record ResetUserPasswordRequest(int UserId, string NewPassword);

public record SetUserActiveRequest(int UserId, bool IsActive);
