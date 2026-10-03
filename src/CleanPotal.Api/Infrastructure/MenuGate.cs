using CleanPotal.Core;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 이 컨트롤러는 한 메뉴 화면만 쓴다는 표시. 관리자가 그 메뉴를 사용자에게 숨기면(User.HiddenMenus)
/// 서버도 그 사용자의 요청을 403 으로 막는다. 예전에는 사이드바에서만 가려, API 를 직접 부르면 그대로 쓸 수 있었다.
///
/// 여러 화면이 같이 쓰는 API(업체·회의록·인수인계 등)에는 붙이지 않는다 — 한 메뉴를 숨겼다고
/// 보이는 다른 메뉴까지 깨지면 안 된다.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class MenuGateAttribute : Attribute
{
    public string Route { get; }
    public MenuGateAttribute(string route) => Route = route;
}

/// <summary>
/// 편집 동작이 어느 메뉴의 것인지만 알리는 표시 — 메뉴 '조회만'(User.ReadOnlyMenus) 판정에만 쓴다.
/// 숨긴 메뉴 차단(<see cref="MenuGateAttribute"/>)은 걸지 않는다: 업체·공지·배차처럼 다른 화면이 같이 읽는 API 에
/// 숨김까지 걸면 그 화면이 깨지지만, 편집만 막는 것은 안전하다. 여러 개 붙이면 그중 하나라도 조회만이면 막는다.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class EditGateAttribute : Attribute
{
    public string Route { get; }
    public EditGateAttribute(string route) => Route = route;
}

public sealed class MenuGateFilter : IAsyncActionFilter
{
    private readonly CleanPotalDbContext _db;
    public MenuGateFilter(CleanPotalDbContext db) => _db = db;

    /// <summary>편집 정책이 없는 동작에서 정책으로 메뉴를 짐작한다 — 근무표 도장과 일정 등록이 한 컨트롤러에 있다.</summary>
    private static readonly Dictionary<string, string> PolicyMenu = new(StringComparer.Ordinal)
    {
        ["EditRoster"] = "/roster",
        ["EditSchedule"] = "/calendar",
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var meta = context.ActionDescriptor.EndpointMetadata;
        var gate = meta.OfType<MenuGateAttribute>().LastOrDefault();
        // 편집 동작 = 편집 등급 정책(Edit…)이 붙은 동작. 조회 등급으로 하게 한 쓰기(QR 점검 제출 등)는 조회만이어도 된다.
        var editPolicies = meta.OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Policy ?? "").Where(p => p.StartsWith("Edit", StringComparison.Ordinal)).ToList();
        var editRoutes = editPolicies.Count == 0 ? new List<string>()
            : meta.OfType<EditGateAttribute>().Select(e => e.Route)
                .Concat(gate is null ? Enumerable.Empty<string>() : new[] { gate.Route })
                .Concat(editPolicies.Where(PolicyMenu.ContainsKey).Select(p => PolicyMenu[p]))
                .Distinct().ToList();

        // 사외 차단 메뉴(관리자 › 외부 접속 보안) — 관리자도 사외에서는 열지 못한다.
        if (gate is not null && ExternalAccessPolicy.Restricted(context.HttpContext) is { } extHidden && extHidden.Contains(gate.Route))
            throw new ForbiddenException("사외에서는 열 수 없는 메뉴입니다. 사내(회사 와이파이)에서 이용하세요.");

        if ((gate is not null || editRoutes.Count > 0) && int.TryParse(context.HttpContext.User.FindFirst("uid")?.Value, out var uid))
        {
            var items = context.HttpContext.Items;
            if (items["auth_user"] is not User user)
            {
                user = (await _db.Users.FindAsync(uid))!;
                items["auth_user"] = user;
            }
            if (user is { IsAdmin: false })
            {
                if (gate is not null && IsHidden(user.HiddenMenus, gate.Route))
                    throw new ForbiddenException("관리자가 숨겨 둔 메뉴입니다. 필요하면 관리자에게 요청하세요.");
                if (editRoutes.Any(r => IsHidden(user.ReadOnlyMenus, r)))
                    throw new ForbiddenException("이 메뉴는 조회만 할 수 있습니다. 고쳐야 하면 관리자에게 요청하세요.");
            }
        }
        await next();
    }

    /// <summary>이 사용자에게 그 메뉴가 '조회만'인가(관리자는 아니다). 한 API 를 두 메뉴가 나눠 쓰는 곳(기타/주간세정·회의록/주간보고)에서 직접 쓴다.</summary>
    public static bool IsReadOnly(Microsoft.AspNetCore.Http.HttpContext http, string route)
        => http.Items["auth_user"] is User { IsAdmin: false } u && IsHidden(u.ReadOnlyMenus, route);

    public static bool IsHidden(string? hiddenMenusJson, string route)
    {
        if (string.IsNullOrWhiteSpace(hiddenMenusJson)) return false;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(hiddenMenusJson);
            return arr is not null && arr.Contains(route, StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
}
