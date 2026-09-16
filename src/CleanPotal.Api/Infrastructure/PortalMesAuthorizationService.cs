using CleanPotal.Core;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
///
/// 일반 사용자는 두 곳을 합쳐서 본다.
///   1. 포털 사용자 권한 화면에서 켜 준 것(Users.MesPermissions) — 지금 관리자가 쓰는 곳.
///   2. 데스크톱판이 남긴 MES 권한 행(MesUserPermissions) — 예전에 주던 권한.
/// 합치는 이유는 옮기는 중에 어느 한쪽이 비어 권한이 사라지는 일을 막기 위해서다. 둘 중 하나에만
/// 있어도 준 것으로 본다 — 권한을 거두는 일은 포털 화면에서 하고, 예전 행은 건드리지 않는다.
/// </summary>
public sealed class PortalMesAuthorizationService : IAuthorizationService
{
    private readonly ICurrentUser _portalUser;
    private readonly ICurrentUserProvider _account;
    private readonly IRepository<User, int> _users;
    private readonly IRepository<UserPermission, int> _permissions;
    private readonly CleanPotalDbContext _portal;

    public PortalMesAuthorizationService(
        ICurrentUser portalUser,
        ICurrentUserProvider account,
        IRepository<User, int> users,
        IRepository<UserPermission, int> permissions,
        CleanPotalDbContext portal)
    {
        _portalUser = portalUser;
        _account = account;
        _users = users;
        _permissions = permissions;
        _portal = portal;
    }

    public async Task<CurrentUserPermissions> GetCurrentUserPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var account = _account.GetCurrentUser();

        if (_portalUser.IsAdmin)
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());

        var granted = new HashSet<PermissionCode>();

        // 1. 포털 권한 화면에서 켜 준 것.
        var stored = await _portal.Users
            .Where(u => u.Username == account)
            .Select(u => u.MesPermissions)
            .FirstOrDefaultAsync(cancellationToken);
        foreach (var name in MesPermissionCodes.Parse(stored))
        {
            if (Enum.TryParse<PermissionCode>(name, out var code)) { granted.Add(code); }
        }

        // 2. 데스크톱판이 남긴 MES 권한 행. MES 계정이 없거나 꺼져 있으면 1번만으로 본다.
        var user = (await _users.ListAsync(u => u.LoginId == account, cancellationToken)).FirstOrDefault();
        if (user is not null && user.IsActive)
        {
            if (user.Role == UserRole.Admin)
                return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());

            foreach (var row in await _permissions.ListAsync(p => p.UserId == user.Id, cancellationToken))
            {
                granted.Add(row.PermissionCode);
            }
        }

        return new CurrentUserPermissions(account, IsAdmin: false, Granted: granted);
    }

    public async Task EnsurePermissionAsync(PermissionCode code, CancellationToken cancellationToken = default)
    {
        var permissions = await GetCurrentUserPermissionsAsync(cancellationToken);
        if (!permissions.Has(code))
            throw new UnauthorizedException($"이 작업을 수행할 권한이 없습니다: {code}");
    }
}
