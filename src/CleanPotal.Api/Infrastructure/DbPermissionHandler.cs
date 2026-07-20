using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// DB 기반 권한 검증. 권한을 JWT 클레임이 아니라 매 요청 DB에서 조회하므로
/// 관리자가 권한을 바꾸면 당사자 재로그인 없이 즉시 반영된다.
/// </summary>
public class DbPermissionRequirement : IAuthorizationRequirement
{
    public string Perm { get; }
    public DbPermissionRequirement(string perm) => Perm = perm;
}

public class DbPermissionHandler : AuthorizationHandler<DbPermissionRequirement>
{
    private readonly CleanPotalDbContext _db;
    private readonly IHttpContextAccessor _http;
    public DbPermissionHandler(CleanPotalDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DbPermissionRequirement requirement)
    {
        if (!int.TryParse(context.User.FindFirst("uid")?.Value, out var uid)) return;

        // 같은 요청에서 정책이 여러 번 평가돼도 사용자 조회는 1회만
        var items = _http.HttpContext?.Items;
        User? user = items?["auth_user"] as User;
        if (user is null)
        {
            user = await _db.Users.FindAsync(uid);
            if (items is not null) items["auth_user"] = user;
        }
        if (user is null || user.IsResigned) return;

        bool ok = user.IsAdmin || requirement.Perm switch
        {
            "files" => user.CanManageFiles,
            "notices" => user.CanManageNotices,
            "vendors" => user.CanManageVendors,
            "schedule" => user.CanManageSchedule,
            "broken" => user.CanManageBroken,
            "etc" => user.CanAccessEtcMenu,
            "shiftboard" => user.CanManageShiftBoard,
            "inventory" => user.CanManageInventory,
            "admin" => false,   // admin 전용은 IsAdmin으로만 통과
            _ => false,
        };
        if (ok) context.Succeed(requirement);
    }
}
