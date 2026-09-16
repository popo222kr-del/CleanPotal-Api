using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 계정과 권한 관리. "셋업 > 사용자 관리" 화면이 쓴다.
// 계정 생성·비밀번호 초기화·활성 토글·등급 변경과, 화면별 권한(PermissionCode) 부여를 함께 다룬다.
public interface IUserPermissionService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserDetailDto> GetDetailAsync(int userId, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(UpdateUserRoleRequest request, CancellationToken cancellationToken = default);
    Task SetPermissionAsync(SetUserPermissionRequest request, CancellationToken cancellationToken = default);

    // 2026-08-28: 아이디/비밀번호 계정 관리(생성/비밀번호 초기화/활성 토글).
    Task<int> CreateAccountAsync(CreateUserAccountRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetUserPasswordRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(SetUserActiveRequest request, CancellationToken cancellationToken = default);

    // 2026-08-31 피드백(#5): 본인(또는 관리자) 계정 정보(아이디/이름/팀명) 수정.
    Task UpdateProfileAsync(UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
}
