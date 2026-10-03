using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// DB 기반 영역×등급 권한 검증 (0=없음/1=조회/2=편집).
/// 매 요청 DB에서 조회하므로 등급 변경이 재로그인 없이 즉시 반영된다.
/// 영역: schedule(일정)·roster(근무표)·handover(세정 작업·인수인계)·field(설비·공정 관리)·material(자재·물류)·office(OFFICE)
///       worklog(생산 설비 목록 = field 또는 office)
///       vendors(업체 관리 = handover 또는 office)
///       mes(생산관리 — LOT 현황·공정·전산등록)
///       admin(관리자 전용)·reports(회의록/보고서 = handover 또는 office)
///       attach(첨부 보관소 = 어느 영역이든 그 등급이면)
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
        // 관리자 영역은 사외에서 늘 막는다(외부 접속 제한이 켜져 있을 때 — 토큰 검증에서 표시해 둔다).
        if (requirement.Area == "admin" && ExternalAccessPolicy.Restricted(_http.HttpContext) is not null) return;
        if (user.IsAdmin) { context.Succeed(requirement); return; }   // 관리자 = 전체 통과

        bool ok = requirement.Area switch
        {
            "schedule" => user.AccessSchedule >= requirement.MinLevel,
            "roster" => user.AccessRoster >= requirement.MinLevel,
            "handover" => user.AccessHandover >= requirement.MinLevel,
            "field" => user.AccessField >= requirement.MinLevel,
            "material" => user.AccessMaterial >= requirement.MinLevel,
            // 생산 설비 목록은 설비·공정 기록 화면과 Daily 업무 보고(OFFICE)가 같이 쓴다
            "worklog" => user.AccessField >= requirement.MinLevel || user.AccessOffice >= requirement.MinLevel,
            "office" => user.AccessOffice >= requirement.MinLevel,
            "mes" => user.AccessMes >= requirement.MinLevel,
            // 회의록/보고서 API는 생산미팅(인수인계)과 주간보고(OFFICE)가 공유
            "reports" => user.AccessHandover >= requirement.MinLevel || user.AccessOffice >= requirement.MinLevel,
            // 업체 관리는 OFFICE 메뉴에 있지만 기타세정 현황(인수인계)에서도 들어간다 — 둘 중 하나면 된다.
            "vendors" => user.AccessHandover >= requirement.MinLevel || user.AccessOffice >= requirement.MinLevel,
            // 첨부 보관소는 화면 여럿이 같이 쓴다. 한 영역으로 묶을 수 없으므로
            // "어디든 무언가를 고칠 수 있는 사람" 을 기준으로 삼는다.
            // 기록 자체를 저장하는 일은 그 화면의 API 가 따로 가른다.
            "attach" => user.AccessSchedule >= requirement.MinLevel
                        || user.AccessRoster >= requirement.MinLevel
                        || user.AccessHandover >= requirement.MinLevel
                        || user.AccessField >= requirement.MinLevel
                        || user.AccessMaterial >= requirement.MinLevel
                        || user.AccessOffice >= requirement.MinLevel
                        || user.AccessMes >= requirement.MinLevel,
            "admin" => false,   // 관리자 전용은 IsAdmin으로만 통과
            _ => false,
        };
        if (ok) context.Succeed(requirement);
    }
}
