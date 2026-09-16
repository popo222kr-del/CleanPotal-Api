using CleanPotal.Core.Interfaces;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// MES 셋업 화면의 세부 권한(제품·업체·공정 마스터)을 포털 로그인에 연결한다.
///
/// 포털 관리자는 그대로 MES 관리자로 본다 — 포털에서 관리자에게 MES 마스터를 맡겨 놓고
/// MES 쪽에 계정 행이 없다고 막으면, 아무도 마스터를 못 고치는 상태가 된다.
/// 일반 사용자는 기존 MES 권한 행(MesUserPermissions)을 그대로 읽는다. 데스크톱판에서 주던 권한이
/// 그대로 살아 있고, 나중에 포털 권한 화면으로 옮길 때도 데이터를 버리지 않는다.
/// </summary>
public sealed class PortalMesAuthorizationService : IAuthorizationService
{
    private readonly ICurrentUser _portalUser;
    private readonly ICurrentUserProvider _account;
    private readonly IRepository<User, int> _users;
    private readonly IRepository<UserPermission, int> _permissions;

    public PortalMesAuthorizationService(
        ICurrentUser portalUser,
        ICurrentUserProvider account,
        IRepository<User, int> users,
        IRepository<UserPermission, int> permissions)
    {
        _portalUser = portalUser;
        _account = account;
        _users = users;
        _permissions = permissions;
    }

    public async Task<CurrentUserPermissions> GetCurrentUserPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var account = _account.GetCurrentUser();

        if (_portalUser.IsAdmin)
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());

        var user = (await _users.ListAsync(u => u.LoginId == account, cancellationToken)).FirstOrDefault();
        if (user is null || !user.IsActive)
            return new CurrentUserPermissions(account, IsAdmin: false, Granted: new HashSet<PermissionCode>());

        if (user.Role == UserRole.Admin)
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());

        var granted = await _permissions.ListAsync(p => p.UserId == user.Id, cancellationToken);
        return new CurrentUserPermissions(account, IsAdmin: false, Granted: granted.Select(p => p.PermissionCode).ToHashSet());
    }

    public async Task EnsurePermissionAsync(PermissionCode code, CancellationToken cancellationToken = default)
    {
        var permissions = await GetCurrentUserPermissionsAsync(cancellationToken);
        if (!permissions.Has(code))
            throw new UnauthorizedException($"이 작업을 수행할 권한이 없습니다: {code}");
    }
}
