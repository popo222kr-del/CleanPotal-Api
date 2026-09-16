using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// MES(ProductionManagement)를 포털 프로세스 안에서 쓰기 위한 배선.
///
/// MES 는 화면(Blazor)·업무 계층·DB 계층으로 나뉘어 있고, 업무·DB 계층은 화면에 기대지 않는다.
/// 그래서 화면만 React 로 새로 만들고 업무 로직(LOT 채번·공정 이동 규칙·이력 조회 등)은
/// 그대로 가져다 쓴다 — 같은 규칙을 두 번 구현하면 두 곳이 갈라지기 때문이다.
///
/// 바꿔 끼우는 것은 "지금 누가 작업 중인가" 하나뿐이다(<see cref="PortalCurrentUserProvider"/>).
/// </summary>
public static class MesModule
{
    /// <param name="connectionString">포털이 이미 정한 값. 설정이 비어 있을 때 포털이 만들어 쓰는
    /// SQLite 파일 경로까지 포함한다 — 여기서 설정을 다시 읽으면 두 DB 로 갈린다.</param>
    public static IServiceCollection AddMes(this IServiceCollection services, string connectionString, bool useSqlite)
    {
        // MES 자신의 등록을 그대로 부른다. DB 만 포털이 정한 것을 쓰게 넘겨준다.
        ProductionManagement.Infrastructure.DependencyInjection.AddInfrastructure(services, connectionString, useSqlite);
        ProductionManagement.Application.DependencyInjection.AddApplication(services);

        // MES 가 등록해 둔 세션 사용자 제공자는 Singleton 이다 — 프로세스에 한 명뿐인 데스크톱판 전제다.
        // 포털은 요청마다 사용자가 다르므로 반드시 갈아끼운다(마지막 등록이 이긴다).
        services.AddScoped<ICurrentUserProvider, PortalCurrentUserProvider>();

        // LOT 바코드·QR. 상태가 없어 Singleton 이면 충분하다.
        services.AddSingleton<ProductionManagement.Infrastructure.Imaging.BarcodeService>();

        // 셋업 화면의 세부 권한(제품·업체·공정 마스터)을 포털 로그인에 연결한다.
        // MES 것을 그대로 두면 포털 관리자가 MES 계정 행이 없다는 이유로 마스터를 못 고친다.
        services.AddScoped<ProductionManagement.Application.Interfaces.IAuthorizationService, PortalMesAuthorizationService>();

        // 런시트(공정 진행표) xlsx 생성.
        services.AddScoped<IRunsheetGenerator, ProductionManagement.Infrastructure.Excel.RunsheetExcelGenerator>();

        // 성적서 Excel 채우기는 Excel COM 이라 서버에서 돌릴 수 없다. MES 웹판과 마찬가지로 자리만 채운다
        // — 이게 없으면 CertificateFillService 를 만들 수 없고, 있어도 특이사항 이미지 삽입은 실패로 답한다.
        services.AddScoped<ICertificateExcelFiller, ProductionManagement.Infrastructure.Excel.NoOpCertificateExcelFiller>();
        return services;
    }

    /// <summary>
    /// MES 테이블이 아직 없으면 만들고, 기준 데이터를 채운다. 포털 스키마 준비 직후에 부른다.
    ///
    /// 기준 데이터(공정 10단계 · 공정 플로우 · TRAN 전이 정의 · 사유코드 · LINE · 레시피 정의 ·
    /// 파라미터 카탈로그)는 <b>셋업 화면으로 만들 수 없다</b> — 공정의 OPER 코드나 TRAN 전이 정의에는
    /// 편집 화면이 아예 없다. 이것이 비어 있으면 OPER 에서 아무 공정도 실행할 수 없으므로,
    /// 개발용 샘플과 달리 환경을 가리지 않고 넣는다. 이미 있으면 지나간다.
    /// </summary>
    public static void EnsureSchema(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<ApplicationDbContext>();
        MesSchemaInitializer.EnsureAsync(db).GetAwaiter().GetResult();
        MesBaseDataSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }
}
