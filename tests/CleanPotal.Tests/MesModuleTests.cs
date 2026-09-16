using CleanPotal.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// MES 업무 계층을 포털 프로세스 안에 들일 때의 배선.
///
/// MES 는 원래 한 사람이 쓰는 데스크톱 프로그램이라 "지금 로그인한 사람"을 Singleton 에 담아 뒀다.
/// 그대로 포털에 들이면 서버에 한 명만 존재하게 되어, 누가 작업하든 이력의 작업자가 같은 사람으로
/// 남는다(먼저 들어온 사람 기준). 여기서 그 등록이 확실히 덮였는지 못 박아 둔다.
/// </summary>
public class MesModuleTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        // 연결 문자열은 포털이 정해서 넘긴다 — 여기서는 열지 않으므로 내용은 중요하지 않다.
        MesModule.AddMes(services, "Data Source=:memory:", useSqlite: true);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void 현재_사용자는_요청마다_달라진다()
    {
        using var sp = Build();

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var b = scope2.ServiceProvider.GetRequiredService<ICurrentUserProvider>();

        // 포털용으로 갈아끼운 구현이어야 하고(= MES 의 세션 Singleton 이 아니어야 하고),
        Assert.IsType<PortalCurrentUserProvider>(a);
        // 요청(스코프)마다 다른 인스턴스여야 한다.
        Assert.NotSame(a, b);
    }

    [Fact]
    public void 로그인_정보가_없으면_SYSTEM_으로_기록된다()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();

        // 배치·시드처럼 HttpContext 가 없는 경로. 예외로 죽지 않고 MES 관례대로 SYSTEM 이 된다.
        Assert.Equal("SYSTEM", provider.GetCurrentUser());
    }

    [Fact]
    public void MES_업무_서비스와_DB_가_함께_등록된다()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        // 첫 화면(LOT 현황 조회)이 쓰는 서비스가 의존성까지 전부 채워져 만들어지는지 —
        // 리포지터리 하나라도 빠져 있으면 여기서 터진다.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILotHistoryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
