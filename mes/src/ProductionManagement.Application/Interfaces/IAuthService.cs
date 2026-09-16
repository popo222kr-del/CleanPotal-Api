using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Interfaces;

// 로그인한 세션 사용자 정보.
public record AuthUser(int UserId, string LoginId, string? DisplayName, UserRole Role, bool MustChangePassword);

// 아이디/비밀번호 인증. 계정 생성/권한은 IUserPermissionService가 담당한다.
public interface IAuthService
{
    // 성공 시 세션 사용자, 실패(계정 없음/비활성/비번 불일치) 시 null.
    Task<AuthUser?> LoginAsync(string loginId, string password, CancellationToken cancellationToken = default);

    // 현재 비밀번호 확인 후 새 비밀번호로 변경(MustChangePassword 해제).
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    // 로그인 계정이 하나도 없으면 최초 관리자(마스터)를 시드한다. 운영/개발 공통으로 앱 시작 시 호출한다.
    Task EnsureDefaultAdminAsync(CancellationToken cancellationToken = default);
}
