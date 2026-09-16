using CleanPotal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Enums;
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
    private static ServiceProvider Build(CleanPotal.Core.Interfaces.ICurrentUser? portalUser = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        // 포털이 요청마다 넣어 주는 현재 사용자. MES 쪽 권한 판정이 이것을 본다.
        services.AddScoped<CleanPotal.Core.Interfaces.ICurrentUser>(_ => portalUser ?? FakeCurrentUser.Anonymous());
        // 첨부파일 루트는 포털이 시작할 때 정해 준다(Program.cs). 없으면 파일 저장 서비스가 예외를 던지므로
        // 테스트에서도 같은 값을 채워 둔다 — 여기서 파일을 쓰지는 않는다.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Documents:RootPath"] = Path.Combine(Path.GetTempPath(), "mes-test-documents"),
            })
            .Build());
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

    /// <summary>
    /// 포털 MES 화면들이 쓰는 서비스가 전부, 의존성까지 채워져 만들어지는지.
    ///
    /// 이 테스트가 없으면 등록 하나가 빠져도 빌드는 지나가고, 그 화면을 실제로 연 사람이
    /// 500 을 본다. 컨트롤러가 생성자로 받는 것을 여기 그대로 적어 둔다.
    /// </summary>
    [Theory]
    [InlineData(typeof(ApplicationDbContext))]
    [InlineData(typeof(ILotHistoryService))]          // LOT 현황 조회 · LOT 스캔 · 런시트
    [InlineData(typeof(ILotService))]                 // 대시보드 · TAT · Batch · 입출고
    [InlineData(typeof(IOperQueryService))]           // OPER 목록
    [InlineData(typeof(IInspectionService))]          // OPER 검사값 패널
    [InlineData(typeof(ITranDefinitionService))]      // OPER TRAN·사유코드
    [InlineData(typeof(IOperActionService))]          // OPER 실행
    [InlineData(typeof(IProductReferenceDataService))]// 레시피·파라미터
    [InlineData(typeof(IRegistrationService))]        // 전산등록
    [InlineData(typeof(IProcessDefinitionService))]   // 공정·플로우
    [InlineData(typeof(IProductService))]             // 제품 마스터
    [InlineData(typeof(ICustomerService))]            // 업체 마스터
    [InlineData(typeof(IProductFlowService))]         // 제품별 플로우
    [InlineData(typeof(IProductPriceService))]        // 단가·이미지·성적서 양식
    [InlineData(typeof(IDocumentService))]            // 성적서
    [InlineData(typeof(ICertificateFillService))]     // 특이사항 이미지 삽입
    [InlineData(typeof(IRunsheetGenerator))]          // 런시트 xlsx
    [InlineData(typeof(IHoldService))]                // HOLD 관리
    [InlineData(typeof(IReworkService))]              // 재작업 관리
    [InlineData(typeof(IProcessHistoryQueryRepository))] // 세정 이력 · 이력 삭제
    [InlineData(typeof(IProcessHistoryVoidService))]  // 이력 무효화
    [InlineData(typeof(IAuditLogQueryService))]       // 감사 로그
    [InlineData(typeof(ProductionManagement.Application.Interfaces.IAuthorizationService))] // 셋업 권한
    [InlineData(typeof(ProductionManagement.Infrastructure.Imaging.BarcodeService))]        // 바코드·QR
    public void 포털_MES_화면이_쓰는_서비스는_전부_만들어진다(Type service)
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService(service));
    }

    /// <summary>
    /// 기준 데이터(공정 10단계 · TRAN 전이 정의 · 사유코드 …)가 실제로 깔리는지.
    ///
    /// 이것이 비어 있으면 OPER 에서 아무 공정도 실행할 수 없는데, 셋업 화면으로는 만들 수 없다
    /// — 공정의 OPER 코드나 TRAN 전이 정의에는 편집 화면이 아예 없다.
    /// 두 번 불러도 늘어나지 않아야 한다(서버는 뜰 때마다 부른다).
    /// </summary>
    [Fact]
    public async Task 기준_데이터가_깔리고_다시_불러도_늘어나지_않는다()
    {
        // SQLite 파일 하나를 열어 실제로 넣어 본다(:memory: 는 연결이 끊기면 사라진다).
        var file = Path.Combine(Path.GetTempPath(), $"mes-seed-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={file}"));
            using var sp = services.BuildServiceProvider();

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();

            await MesBaseDataSeeder.SeedAsync(db);
            var processes = await db.ProcessDefinitions.CountAsync();
            var trans = await db.ProcessTransitionDefinitions.CountAsync();
            var reasons = await db.ReasonCodes.CountAsync();

            // 공정 10단계 · TRAN 전이 · 사유코드가 실제로 들어갔다.
            Assert.Equal(10, processes);
            Assert.True(trans > 0, "TRAN 전이 정의가 비어 있으면 어떤 공정도 실행할 수 없다.");
            Assert.True(reasons > 0, "사유코드가 비어 있으면 HOLD·재작업을 걸 수 없다.");
            Assert.NotNull(await db.ProcessRoutes.FirstOrDefaultAsync(r => r.RouteCode == "STANDARD"));

            // 서버가 뜰 때마다 부르므로 두 번째 호출이 같은 것을 또 넣으면 안 된다.
            await MesBaseDataSeeder.SeedAsync(db);
            Assert.Equal(processes, await db.ProcessDefinitions.CountAsync());
            Assert.Equal(trans, await db.ProcessTransitionDefinitions.CountAsync());
            Assert.Equal(reasons, await db.ReasonCodes.CountAsync());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* 임시 파일은 남아도 된다 */ }
        }
    }

    [Fact]
    public void 셋업_권한은_포털용_구현으로_바뀐다()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        // MES 것을 그대로 쓰면 포털 관리자가 MES 계정 행이 없다는 이유로 마스터를 못 고친다.
        var authorization = scope.ServiceProvider
            .GetRequiredService<ProductionManagement.Application.Interfaces.IAuthorizationService>();
        Assert.IsType<PortalMesAuthorizationService>(authorization);
    }

    [Fact]
    public async Task 포털_관리자는_MES_계정_행이_없어도_셋업_권한을_갖는다()
    {
        using var sp = Build(FakeCurrentUser.Admin());
        using var scope = sp.CreateScope();
        var authorization = scope.ServiceProvider
            .GetRequiredService<ProductionManagement.Application.Interfaces.IAuthorizationService>();

        // DB 를 열지 않고 관리자로 판정되어야 한다 — 열면 :memory: 라 아무 행도 없다.
        var permissions = await authorization.GetCurrentUserPermissionsAsync();

        Assert.True(permissions.IsAdmin);
        Assert.True(permissions.Has(PermissionCode.AdminProduct));
        Assert.True(permissions.Has(PermissionCode.AdminCustomer));
        Assert.True(permissions.Has(PermissionCode.AdminProcess));
    }
}
