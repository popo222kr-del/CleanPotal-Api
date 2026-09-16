using System.Security.Claims;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// MES 업무 서비스가 "지금 누가 작업 중인가"를 묻는 통로를 포털 로그인에 연결한다.
///
/// 돌려주는 값은 포털 Username(사번)이다. MES 이력의 작업자 칸과 포털 계정이 같은 축이어야
/// 나중에 "이 사람이 한 작업"을 두 시스템에서 같이 셀 수 있다.
/// 토큰이 없는 경로(배치·시드)에서는 SYSTEM 으로 떨어진다 — MES 데스크톱판과 같은 관례다.
/// </summary>
public sealed class PortalCurrentUserProvider : ICurrentUserProvider
{
    private readonly IHttpContextAccessor _http;
    public PortalCurrentUserProvider(IHttpContextAccessor http) => _http = http;

    public string GetCurrentUser()
    {
        var user = _http.HttpContext?.User;
        if (user is null) return "SYSTEM";
        // 토큰의 sub(=포털 Username). 기본 클레임 매핑이 켜져 있으면 NameIdentifier 로 들어온다.
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "SYSTEM";
    }
}
