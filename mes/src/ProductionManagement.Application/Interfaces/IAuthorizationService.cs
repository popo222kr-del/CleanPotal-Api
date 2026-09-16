using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Interfaces;

// 화면 접근 권한 판정. 화면은 GetCurrentUserPermissions로 메뉴를 켜고 끄고,
// 서비스는 쓰기 메서드 첫 줄에서 EnsurePermissionAsync로 한 번 더 막는다(버튼을 감추는 것만으로는
// 부족하므로 이중으로 건다).
public interface IAuthorizationService
{
    Task<CurrentUserPermissions> GetCurrentUserPermissionsAsync(CancellationToken cancellationToken = default);

    // 권한이 없으면 UnauthorizedException을 던진다 - 쓰기 메서드 맨 앞에서 호출하는 용도.
    Task EnsurePermissionAsync(PermissionCode code, CancellationToken cancellationToken = default);
}

// IsAdmin이면 Granted를 보지 않고 전부 허용한다(Has 참고) - 관리자는 UserPermission 행이 없어도 된다.
public record CurrentUserPermissions(string WindowsAccount, bool IsAdmin, IReadOnlySet<PermissionCode> Granted)
{
    public bool Has(PermissionCode code) => IsAdmin || Granted.Contains(code);
}
