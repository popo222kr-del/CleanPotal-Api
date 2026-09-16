using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Web.Security;

// 웹판 ICurrentUserProvider. 데스크톱은 로그인 결과를 담는 싱글턴(SessionCurrentUserProvider)을 썼지만,
// 웹은 "지금 이 요청/서킷의 로그인 사용자"가 달라야 하므로 Scoped로 등록해 인증 상태에서 LoginId를 읽는다.
//
// Blazor Server의 대화형 서킷에서는 IHttpContextAccessor.HttpContext가 null이 되므로(초기 렌더 이후)
// HttpContext가 아니라 AuthenticationStateProvider(서킷 스코프에 존재)에서 사용자를 읽는 것이 정석이다.
// 로그인 시 심어둔 커스텀 클레임("LoginId")을 우선 사용하고, 없으면 Identity.Name, 그것도 없으면 SYSTEM.
public sealed class BlazorCurrentUserProvider : ICurrentUserProvider
{
    public const string LoginIdClaimType = "LoginId";

    private readonly AuthenticationStateProvider _authStateProvider;

    public BlazorCurrentUserProvider(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public string GetCurrentUser()
    {
        // Blazor Server에서 인증 상태 Task는 서킷 시작 시 확정되어 캐시되므로 여기서 블로킹되지 않는다.
        var state = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult();
        var user = state.User;
        return user.FindFirst(LoginIdClaimType)?.Value
               ?? user.Identity?.Name
               ?? "SYSTEM";
    }
}
