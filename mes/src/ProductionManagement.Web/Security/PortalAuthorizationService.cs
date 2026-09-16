using Microsoft.AspNetCore.Components.Authorization;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Web.Security;

// CleanPotal의 관리자 클레임을 MES 관리자 권한으로 사용한다.
// 일반 사용자는 기존 MES 권한 행을 계속 읽으므로, 추후 CleanPotal 권한 화면과 연결할 때 데이터가 유지된다.
public sealed class PortalAuthorizationService : IAuthorizationService
{
    private readonly AuthenticationStateProvider _authenticationState;
    private readonly IRepository<User, int> _users;
    private readonly IRepository<UserPermission, int> _permissions;

    public PortalAuthorizationService(
        AuthenticationStateProvider authenticationState,
        IRepository<User, int> users,
        IRepository<UserPermission, int> permissions)
    {
        _authenticationState = authenticationState;
        _users = users;
        _permissions = permissions;
    }

    public async Task<CurrentUserPermissions> GetCurrentUserPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var principal = (await _authenticationState.GetAuthenticationStateAsync()).User;
        var account = principal.FindFirst(BlazorCurrentUserProvider.LoginIdClaimType)?.Value
            ?? principal.Identity?.Name
            ?? "SYSTEM";

        if (principal.IsInRole("Admin"))
        {
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());
        }

        var user = (await _users.ListAsync(u => u.LoginId == account, cancellationToken)).FirstOrDefault();
        if (user is null || !user.IsActive)
        {
            return new CurrentUserPermissions(account, IsAdmin: false, Granted: new HashSet<PermissionCode>());
        }

        if (user.Role == UserRole.Admin)
        {
            return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());
        }

        var granted = await _permissions.ListAsync(p => p.UserId == user.Id, cancellationToken);
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
