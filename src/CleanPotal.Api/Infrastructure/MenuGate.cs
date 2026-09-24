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

public sealed class MenuGateFilter : IAsyncActionFilter
{
    private readonly CleanPotalDbContext _db;
    public MenuGateFilter(CleanPotalDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gate = context.ActionDescriptor.EndpointMetadata.OfType<MenuGateAttribute>().LastOrDefault();
        if (gate is not null && int.TryParse(context.HttpContext.User.FindFirst("uid")?.Value, out var uid))
        {
            var items = context.HttpContext.Items;
            if (items["auth_user"] is not User user)
            {
                user = (await _db.Users.FindAsync(uid))!;
                items["auth_user"] = user;
            }
            if (user is { IsAdmin: false } && IsHidden(user.HiddenMenus, gate.Route))
                throw new ForbiddenException("관리자가 숨겨 둔 메뉴입니다. 필요하면 관리자에게 요청하세요.");
        }
        await next();
    }

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
