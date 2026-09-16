using System.Reflection;
using CleanPotal.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// MES 엔드포인트의 권한이 빠지지 않았는지.
///
/// 권한 누락은 빌드도 테스트도 못 본다 — 누군가 그 주소를 알아내 부를 때까지 아무 일도 일어나지
/// 않는다. 컨트롤러가 열 개가 됐고 앞으로도 늘 것이라, 규칙을 사람 눈이 아니라 여기서 지킨다.
///
/// 규칙은 둘이다.
/// 1. MES 컨트롤러는 전부 <c>ViewMes</c> 로 잠근다(조회조차 권한이 있어야 한다).
/// 2. 자료를 바꾸는 동작(POST · PUT · DELETE)은 <c>EditMes</c> 가 더 필요하다.
///    조회인데 POST 를 쓰는 예외는 아래 목록에 이유와 함께 적는다.
/// </summary>
public class MesEndpointPolicyTests
{
    /// <summary>
    /// 자료를 바꾸지 않는 POST. 보낼 조건이 많거나(필터·검사값) 파일을 올려야 해서 POST 일 뿐이다.
    /// 새로 생길 때마다 여기에 이유를 적게 해서, 권한을 빠뜨린 것과 구분되게 한다.
    /// </summary>
    private static readonly Dictionary<string, string> ReadOnlyWrites = new()
    {
        ["MesLotController.Decode"] = "사진에서 바코드·QR 을 읽기만 한다(파일을 올려야 해서 POST).",
        ["MesOperController.SpecCheck"] = "지금 입력한 값이 SPEC 을 벗어났는지 보기만 한다.",
        ["MesLotInOutController.Drill"] = "매트릭스 칸 조건으로 목록을 좁혀 볼 뿐이다(조건이 커서 POST).",
        ["MesLotInOutController.DrillInOut"] = "같은 이유.",
    };

    private static IEnumerable<Type> MesControllers() => typeof(MesLotController).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsPublic: true }
                    && t.Name.StartsWith("Mes", StringComparison.Ordinal)
                    && t.Name.EndsWith("Controller", StringComparison.Ordinal)
                    && typeof(ControllerBase).IsAssignableFrom(t));

    [Fact]
    public void MES_컨트롤러는_모두_ViewMes_로_잠겨_있다()
    {
        var open = MesControllers()
            .Where(t => t.GetCustomAttributes<AuthorizeAttribute>().All(a => a.Policy != "ViewMes"))
            .Select(t => t.Name)
            .ToList();

        Assert.True(open.Count == 0,
            "ViewMes 가 걸리지 않은 MES 컨트롤러: " + string.Join(", ", open));
    }

    [Fact]
    public void 자료를_바꾸는_동작은_EditMes_가_필요하다()
    {
        var missing = new List<string>();

        foreach (var controller in MesControllers())
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var writes = action.GetCustomAttributes<HttpPostAttribute>().Any()
                             || action.GetCustomAttributes<HttpPutAttribute>().Any()
                             || action.GetCustomAttributes<HttpDeleteAttribute>().Any()
                             || action.GetCustomAttributes<HttpPatchAttribute>().Any();
                if (!writes) continue;

                var name = $"{controller.Name}.{action.Name}";
                if (ReadOnlyWrites.ContainsKey(name)) continue;

                if (action.GetCustomAttributes<AuthorizeAttribute>().All(a => a.Policy != "EditMes"))
                    missing.Add(name);
            }
        }

        Assert.True(missing.Count == 0,
            "EditMes 가 빠진 쓰기 동작(조회 목적이라면 ReadOnlyWrites 에 이유와 함께 적을 것): "
            + string.Join(", ", missing));
    }

    [Fact]
    public void 예외_목록에_적힌_동작이_실제로_존재한다()
    {
        // 메서드 이름이 바뀌면 예외 목록이 조용히 무력해진다 — 그러면 그 자리는 다시 검사되지 않는다.
        var actual = MesControllers()
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                              .Select(m => $"{c.Name}.{m.Name}"))
            .ToHashSet();

        var stale = ReadOnlyWrites.Keys.Where(k => !actual.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "없는 동작이 예외 목록에 남아 있다: " + string.Join(", ", stale));
    }
}
