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

        // 포털 화면(CleanPotal.Api 의 PortalMesAuthorizationService)과 같은 규칙: 두 곳을 합쳐서 본다.
        //   1. 포털 사용자 권한 화면에서 켜 준 것(Users.MesPermissions) — 로그인 확인 때 쿠키에 담아 두고 5분마다 갱신된다.
        //   2. 데스크톱판이 남긴 MES 권한 행(MesUserPermissions).
        // 예전에는 1번을 보지 않아, 같은 사람이 포털 화면에서는 되고 여기서는 막히는 일이 있었다.
        var granted = new HashSet<PermissionCode>();
        var fromPortal = principal.FindFirst(PortalSession.MesPermissionsClaim)?.Value ?? "";
        foreach (var name in fromPortal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<PermissionCode>(name, out var code)) granted.Add(code);
        }

        var user = (await _users.ListAsync(u => u.LoginId == account, cancellationToken)).FirstOrDefault();
        if (user is not null && user.IsActive)
        {
            if (user.Role == UserRole.Admin)
                return new CurrentUserPermissions(account, IsAdmin: true, Granted: new HashSet<PermissionCode>());

            foreach (var row in await _permissions.ListAsync(p => p.UserId == user.Id, cancellationToken))
                granted.Add(row.PermissionCode);
        }

        return new CurrentUserPermissions(account, IsAdmin: false, Granted: granted);
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
