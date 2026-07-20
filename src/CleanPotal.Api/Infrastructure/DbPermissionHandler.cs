using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// DB 기반 영역×등급 권한 검증 (0=없음/1=조회/2=편집).
/// 매 요청 DB에서 조회하므로 등급 변경이 재로그인 없이 즉시 반영된다.
/// 영역: schedule(일정)·roster(근무표)·handover(현장 인수인계)·field(현장 점검)·office(OFFICE)
///       admin(관리자 전용)·reports(회의록/보고서 = handover 또는 office)
/// </summary>
public class DbPermissionRequirement : IAuthorizationRequirement
{
    public string Area { get; }
    public int MinLevel { get; }
    public DbPermissionRequirement(string area, int minLevel) { Area = area; MinLevel = minLevel; }
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
        if (user.IsAdmin) { context.Succeed(requirement); return; }   // 관리자 = 전체 통과

        bool ok = requirement.Area switch
        {
            "schedule" => user.AccessSchedule >= requirement.MinLevel,
            "roster" => user.AccessRoster >= requirement.MinLevel,
            "handover" => user.AccessHandover >= requirement.MinLevel,
            "field" => user.AccessField >= requirement.MinLevel,
            "office" => user.AccessOffice >= requirement.MinLevel,
            // 회의록/보고서 API는 생산미팅(인수인계)과 주간보고(OFFICE)가 공유
            "reports" => user.AccessHandover >= requirement.MinLevel || user.AccessOffice >= requirement.MinLevel,
            "admin" => false,   // 관리자 전용은 IsAdmin으로만 통과
            _ => false,
        };
        if (ok) context.Succeed(requirement);
    }
}
