using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ProductionManagement.Web.Security;

/// <summary>
/// 포털 로그인 ↔ MES 쿠키 세션.
///
/// MES 는 따로 로그인하지 않는다. 포털 React 화면이 JWT 를 들고 /auth/portal-session 을 부르면, MES 가 그 토큰을
/// 포털의 /api/auth/me 로 확인한 뒤 쿠키를 발급한다. 확인할 포털 주소는 브라우저가 보낸 Host 가 아니라
/// 포털 프록시가 넣어 준 실제 수신 주소만 믿는다(Host 를 바꿔 가짜 "관리자" 응답을 받던 구멍).
///
/// 쿠키에는 포털 토큰을 암호화해 넣어 두고 5분마다 다시 포털에 묻는다. 예전에는 쿠키가 한 번 발급되면 12시간
/// (쓰는 동안 계속 연장) 다시 확인하지 않아, 퇴사·비밀번호 변경·MES 권한 회수·포털 로그아웃 뒤에도 MES 가 열렸다.
/// </summary>
public static class PortalSession
{
    public const string DirectPeerItemKey = "CleanPotal.DirectPeer";
    public const string PortalEndpointHeader = "X-CleanPotal-Portal-Endpoint";
    public const string MesLevelClaim = "MesLevel";
    public const string MesPermissionsClaim = "MesPermissions";

    /// <summary>작업(쓰기)이 있는 화면에 거는 정책 — 포털 MES 편집 등급(2) 또는 관리자.</summary>
    public const string EditPolicy = "MesEdit";

    private const string TokenName = "portal_token";
    private const string CheckedItem = "portal_checked";
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(5);

    public enum LookupStatus { Ok, Unauthorized, Unreachable, NoPortal }

    public sealed record Lookup(LookupStatus Status, PortalUser? User);

    /// <summary>토큰을 확인해 줄 포털 주소. 설정값 > 루프백 프록시가 넣은 실제 수신 주소 > 개발환경 한정 Host.</summary>
    public static (string Base, string? Host)? ResolvePortalBase(HttpContext http)
    {
        var overrideBase = http.RequestServices.GetRequiredService<IConfiguration>()["Portal:ApiBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(overrideBase)) return (overrideBase, null);

        var peer = http.Items[DirectPeerItemKey] as IPAddress;
        if (peer is { IsIPv4MappedToIPv6: true }) peer = peer.MapToIPv4();
        if (peer is not null && IPAddress.IsLoopback(peer)
            && Uri.TryCreate(http.Request.Headers[PortalEndpointHeader].ToString(), UriKind.Absolute, out var endpoint)
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
        {
            // 연결은 고정된 IP:포트로 하고, IIS 바인딩이 호스트 이름을 요구하는 경우를 위해 Host 만 원래 값으로 둔다.
            return (endpoint.GetLeftPart(UriPartial.Authority), http.Request.Host.Value);
        }

        if (http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            return ($"{http.Request.Scheme}://{http.Request.Host}", null);
        return null;
    }

    /// <summary>포털에 "이 토큰의 주인이 누구이며 아직 유효한가" 를 묻는다.</summary>
    public static async Task<Lookup> FetchUserAsync(HttpContext http, string authorization, ILogger log)
    {
        var target = ResolvePortalBase(http);
        if (target is null)
        {
            log.LogWarning("[mes] 포털 주소를 확인할 수 없습니다(프록시 헤더 없음, Portal:ApiBaseUrl 미설정).");
            return new(LookupStatus.NoPortal, null);
        }

        var (portalBase, host) = target.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{portalBase}/api/auth/me");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (!string.IsNullOrEmpty(host)) request.Headers.Host = host;

        try
        {
            var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("CleanPotalApi");
            using var response = await client.SendAsync(request, http.RequestAborted);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(LookupStatus.Unauthorized, null);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("[mes] 포털 사용자 확인 실패 {Status}", (int)response.StatusCode);
                return new(LookupStatus.Unreachable, null);
            }

            // 포털 API 는 모든 응답을 { success, data, error } 봉투로 감싼다(EnvelopeResultFilter).
            var envelope = await response.Content.ReadFromJsonAsync<PortalEnvelope>(http.RequestAborted);
            var user = envelope?.Data;
            if (user is null || user.IsResigned || string.IsNullOrWhiteSpace(user.Username))
                return new(LookupStatus.Unauthorized, null);
            return new(LookupStatus.Ok, user);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "[mes] 포털({Base})에 연결하지 못했습니다.", portalBase);
            return new(LookupStatus.Unreachable, null);
        }
    }

    /// <summary>MES 에 들어올 수 있는가 — 포털 MES 등급 1(조회) 이상 또는 관리자.</summary>
    public static bool MayEnter(PortalUser user) => user.IsAdmin || user.AccessMes >= 1;

    public static bool CanEdit(ClaimsPrincipal user)
        => user.IsInRole("Admin")
           || (int.TryParse(user.FindFirst(MesLevelClaim)?.Value, out var level) && level >= 2);

    public static ClaimsPrincipal BuildPrincipal(PortalUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.RealName),
            new(BlazorCurrentUserProvider.LoginIdClaimType, user.Username),
            new("DisplayName", user.RealName),
            new("Department", user.Department ?? string.Empty),
            new("TeamName", user.TeamName ?? string.Empty),
            new(MesLevelClaim, (user.IsAdmin ? 2 : user.AccessMes).ToString(CultureInfo.InvariantCulture)),
            new(MesPermissionsClaim, user.MesPermissions ?? string.Empty),
        };
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static AuthenticationProperties NewProperties(string token)
    {
        var props = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) };
        props.StoreTokens(new[] { new AuthenticationToken { Name = TokenName, Value = token } });
        props.Items[CheckedItem] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return props;
    }

    /// <summary>쿠키로 들어온 요청마다 불린다. 5분이 지났으면 포털에 다시 묻고, 더는 들어올 수 없으면 쿠키를 지운다.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProductionManagement.Web.PortalSession");
        var props = ctx.Properties;
        var token = props.GetTokenValue(TokenName);
        if (string.IsNullOrEmpty(token))
        {
            // 토큰을 넣기 전에 발급된 옛 쿠키 — 다시 확인할 방법이 없으니 버린다. 포털 화면이 곧 다시 세션을 만든다.
            await RejectAsync(ctx);
            return;
        }

        if (props.Items.TryGetValue(CheckedItem, out var checkedAt)
            && DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            && DateTimeOffset.UtcNow - at < RecheckInterval)
            return;

        var lookup = await FetchUserAsync(ctx.HttpContext, $"Bearer {token}", log);
        // 포털이 잠깐 안 닿는 것은 사용자의 잘못이 아니다 — 세션은 두고 다음 요청에서 다시 묻는다.
        if (lookup.Status is LookupStatus.Unreachable or LookupStatus.NoPortal) return;

        if (lookup.User is not { } user || !MayEnter(user))
        {
            log.LogInformation("[mes] 포털에서 더는 유효하지 않은 세션이라 MES 쿠키를 지웁니다.");
            await RejectAsync(ctx);
            return;
        }

        // 등급·관리자 여부·세부 권한이 바뀌었을 수 있으니 매번 새 값으로 갈아 끼운다.
        ctx.ReplacePrincipal(BuildPrincipal(user));
        props.Items[CheckedItem] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        ctx.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

/// <summary>포털 API 표준 응답 봉투 — 실제 값은 Data 안에 들어 있다.</summary>
public sealed record PortalEnvelope(bool Success, PortalUser? Data, string? Error);

public sealed record PortalUser(
    int Id,
    string Username,
    string RealName,
    string? Department,
    string? TeamName,
    bool IsResigned,
    bool IsAdmin,
    // 포털 mes 영역 등급(0 없음 / 1 조회 / 2 작업). 이 필드가 없는 옛 포털과 붙으면 0 이 되어
    // 모두 막히므로, 그때는 포털을 먼저 올려야 한다(둘은 같이 배포된다).
    int AccessMes,
    // MES 세부 권한 코드(쉼표로 이은 것). 포털 권한 화면에서 켜 준 값이다.
    string? MesPermissions);
