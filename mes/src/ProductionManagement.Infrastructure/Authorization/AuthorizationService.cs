using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Infrastructure.Authorization;

// 화면 접근 권한 판정. "이 사람이 이 기능을 써도 되는가"를 PermissionCode 단위로 답한다.
// 관리자(UserRole.Admin)는 개별 권한 목록을 보지 않고 통과시키고, 나머지는 UserPermission에
// 부여된 코드만 허용한다. 권한이 없으면 UnauthorizedException을 던져서 서비스 진입 자체를 막는다
// - 화면에서 버튼을 감추는 것과 별개로 서버 쪽에서 한 번 더 거른다.
public class AuthorizationService : IAuthorizationService
{
    private readonly IRepository<User, int> _users;
    private readonly IRepository<UserPermission, int> _userPermissions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserProvider _currentUser;

    public AuthorizationService(
        IRepository<User, int> users,
        IRepository<UserPermission, int> userPermissions,
        IUnitOfWork unitOfWork,
        ICurrentUserProvider currentUser)
    {
        _users = users;
        _userPermissions = userPermissions;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<CurrentUserPermissions> GetCurrentUserPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var account = _currentUser.GetCurrentUser();
        var user = (await _users.ListAsync(u => u.LoginId == account, cancellationToken)).FirstOrDefault();

        // 로그인 계정으로만 접근한다 - 계정이 없거나 비활성이면 아무 권한도 없다(자동 프로비저닝 폐지).
        if (user is null || !user.IsActive)
        {
            return new CurrentUserPermissions(account, IsAdmin: false, Granted: new HashSet<PermissionCode>());
        }

        if (user.Role == UserRole.Admin)
        {
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());
        }

        var granted = await _userPermissions.ListAsync(p => p.UserId == user.Id, cancellationToken);
        return new CurrentUserPermissions(account, IsAdmin: false, Granted: granted.Select(p => p.PermissionCode).ToHashSet());
    }

    public async Task EnsurePermissionAsync(PermissionCode code, CancellationToken cancellationToken = default)
    {
        var permissions = await GetCurrentUserPermissionsAsync(cancellationToken);
        if (!permissions.Has(code))
        {
            throw new UnauthorizedException($"이 작업을 수행할 권한이 없습니다: {code}");
        }
    }

}
