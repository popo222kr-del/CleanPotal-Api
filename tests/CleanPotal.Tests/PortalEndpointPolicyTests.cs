using System.Reflection;
using CleanPotal.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 포털 전체(MES 를 뺀 나머지) 엔드포인트의 권한이 빠지지 않았는지.
///
/// 권한 누락은 빌드도 테스트도 못 본다 — 누군가 그 주소를 알아내 부를 때까지 아무 일도 일어나지
/// 않는다. MES 에는 같은 규칙을 지키는 테스트가 있었지만(<c>MesEndpointPolicyTests</c>) 나머지
/// 화면에는 없었다. 컨트롤러를 새로 만들면서 한 줄 빠뜨리는 것을 여기서 잡는다.
///
/// 규칙은 셋이다.
/// 1. 컨트롤러에는 <c>[Authorize]</c> 가 있어야 한다.
/// 2. 정책 없이 "로그인만" 요구하는 컨트롤러는 아래 목록에 적힌 것만이다.
/// 3. 자료를 바꾸는 동작(POST · PUT · DELETE)은 조회보다 높은 권한(Edit… 또는 IsAdmin)이 필요하다.
///    그렇지 않은 것은 아래 목록에 <b>이유와 함께</b> 적는다.
/// </summary>
public class PortalEndpointPolicyTests
{
    /// <summary>
    /// 로그인만 하면 되는 컨트롤러. 화면 여럿이 같이 쓰는 공용 조회라 한 영역에 묶기 어려운 것들이다.
    /// (`docs/permissions.md` 의 "확인이 필요한 항목" 참고 — 업무 판단이 필요한 자리다.)
    /// </summary>
    private static readonly Dictionary<string, string> LoginOnlyControllers = new()
    {
        ["ScheduleController"] = "오늘 현황·교대조는 인수인계 화면들도 같이 쓴다. ViewSchedule 을 걸면 "
                                 + "일정 등급이 0 인 인수인계 사용자의 화면이 깨진다.",
        ["HolidaysController"] = "화면은 /api/schedule/holidays 를 쓴다(이 API 는 호출하는 곳이 없는 레거시).",
        ["DashboardController"] = "대시보드는 누구나 본다. 카드마다 그 메뉴의 조회 권한·숨긴 메뉴를 동작 안에서 따져 "
                                   + "권한이 없는 카드는 비워 보낸다(체크시트=현장 점검, 기타세정·주간세정·생산팀 요청·배차=인수인계, BROKEN=OFFICE).",
        ["AttachmentsController"] = "첨부 보관소는 BROKEN·주간보고 등 화면 여럿이 같이 쓴다. 받기를 한 영역에 "
                                     + "묶으면 조회 등급만 있는 사람이 자기가 볼 수 있는 기록의 첨부를 못 받는다. "
                                     + "올리는 동작(Upload)은 동작 안에서 EditAttachment 를 확인한다(field 영역만 조회 등급).",
    };

    /// <summary>
    /// 조회 등급으로도 할 수 있는 쓰기. 업무상 그렇게 쓰기로 한 자리이고, 화면도 같은 전제로 만들어져 있다.
    /// 새로 생길 때마다 이유를 적게 해서, 권한을 빠뜨린 것과 구분되게 한다.
    /// </summary>
    private static readonly Dictionary<string, string> ViewLevelWrites = new()
    {
        ["AuthController.Login"] = "로그인 자체(아직 아무 권한도 없다).",
        ["AuthController.ChangeCredentials"] = "본인 비밀번호 변경 — 남이 아니라 자기 것만 바꾼다.",
        ["ChecklistController.Submit"] = "체크시트 점검·제출은 생산직이 QR 을 찍어 하는 일상 업무라 현장 점검 조회(1) 등급이면 된다. "
                                         + "NG 조치 완료는 EditField, 양식 관리는 IsAdmin 을 요구한다.",
        ["AttachmentsController.Upload"] = "현장 점검(field) 첨부만 조회 등급이 올린다 — 체크시트 NG·작업 전후 사진을 조회 등급 "
                                           + "생산직이 찍는다. 다른 영역은 동작 안에서 EditAttachment 를 확인한다.",
        ["ChecklistController.Save"] = "체크시트 항목 결과 입력 — Submit 과 같은 이유(조회 등급 생산직이 체크만 한다).",
        ["PortalController.CreateLaunchTicket"] = "조회 가능한 파일을 여는 20초짜리 1회성 실행권만 만들며 업무 자료를 변경하지 않는다.",
        ["PortalController.RedeemLaunchTicket"] = "로컬 도우미가 이미 발급된 1회성 실행권을 경로로 교환한다. "
                                                   + "임의 ID나 경로는 받지 않고 사용 즉시 실행권을 폐기한다.",
    };

    private static IEnumerable<Type> PortalControllers() => typeof(UsersController).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsPublic: true }
                    && t.Name.EndsWith("Controller", StringComparison.Ordinal)
                    && !t.Name.StartsWith("Mes", StringComparison.Ordinal)
                    && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName);

    private static bool IsWrite(MethodInfo action) =>
        action.GetCustomAttributes<HttpPostAttribute>().Any()
        || action.GetCustomAttributes<HttpPutAttribute>().Any()
        || action.GetCustomAttributes<HttpPatchAttribute>().Any()
        || action.GetCustomAttributes<HttpDeleteAttribute>().Any();

    /// <summary>조회보다 높은 권한인가 — 편집 등급이거나 관리자 전용이면 그렇다.</summary>
    private static bool IsAboveView(string? policy) =>
        policy is not null
        && (policy.StartsWith("Edit", StringComparison.Ordinal) || policy == "IsAdmin");

    [Fact]
    public void 모든_컨트롤러에_권한이_걸려_있다()
    {
        var open = PortalControllers()
            .Where(t => !t.GetCustomAttributes<AuthorizeAttribute>().Any())
            // 로그인 컨트롤러만 예외 — 로그인 자체는 열려 있어야 하고, 나머지 동작에 따로 건다.
            .Where(t => t != typeof(AuthController))
            .Select(t => t.Name)
            .ToList();

        Assert.True(open.Count == 0, "[Authorize] 가 없는 컨트롤러: " + string.Join(", ", open));
    }

    [Fact]
    public void 정책_없이_로그인만_요구하는_컨트롤러는_적어_둔_것뿐이다()
    {
        var loginOnly = PortalControllers()
            .Where(t => t != typeof(AuthController))
            .Where(t => t.GetCustomAttributes<AuthorizeAttribute>().Any(a => string.IsNullOrEmpty(a.Policy)))
            .Select(t => t.Name)
            .Where(name => !LoginOnlyControllers.ContainsKey(name))
            .ToList();

        Assert.True(loginOnly.Count == 0,
            "로그인만 요구하는 컨트롤러가 늘었다. 의도한 것이면 목록에 이유와 함께 적어라: "
            + string.Join(", ", loginOnly));
    }

    [Fact]
    public void 자료를_바꾸는_동작은_조회보다_높은_권한이_필요하다()
    {
        var missing = new List<string>();

        foreach (var controller in PortalControllers())
        {
            var classPolicies = controller.GetCustomAttributes<AuthorizeAttribute>()
                .Select(a => a.Policy).ToList();

            foreach (var action in Actions(controller).Where(IsWrite))
            {
                var key = $"{controller.Name}.{action.Name}";
                if (ViewLevelWrites.ContainsKey(key)) { continue; }

                var actionPolicies = action.GetCustomAttributes<AuthorizeAttribute>()
                    .Select(a => a.Policy).ToList();

                if (actionPolicies.Any(IsAboveView) || classPolicies.Any(IsAboveView)) { continue; }

                missing.Add(key);
            }
        }

        Assert.True(missing.Count == 0,
            "조회 등급으로도 자료를 바꿀 수 있는 동작이다. 의도한 것이면 목록에 이유와 함께 적어라: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void 예외_목록에_적힌_것이_실제로_존재한다()
    {
        // 메서드 이름이 바뀌거나 컨트롤러가 사라지면 예외 목록이 조용히 무력해진다.
        var controllers = PortalControllers().ToDictionary(t => t.Name);

        foreach (var name in LoginOnlyControllers.Keys)
        {
            Assert.True(controllers.ContainsKey(name), $"예외 목록의 {name} 이(가) 없다.");
        }

        foreach (var key in ViewLevelWrites.Keys)
        {
            var parts = key.Split('.');
            Assert.True(controllers.TryGetValue(parts[0], out var controller), $"예외 목록의 {parts[0]} 이(가) 없다.");
            Assert.True(Actions(controller!).Any(m => m.Name == parts[1]), $"예외 목록의 {key} 이(가) 없다.");
        }
    }
}
