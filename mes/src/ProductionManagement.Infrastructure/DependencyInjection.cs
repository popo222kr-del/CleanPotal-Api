using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Authorization;
using ProductionManagement.Infrastructure.Data;
using ProductionManagement.Infrastructure.FileStorage;
using ProductionManagement.Infrastructure.Repositories;
using ProductionManagement.Infrastructure.Sequencing;

namespace ProductionManagement.Infrastructure;

// Infrastructure 계층의 서비스 등록을 한곳에 모은 곳. 앱(App.xaml.cs)은 AddInfrastructure 한 줄만
// 부르면 되고, "무엇을 어떤 구현으로 쓸지"는 전부 여기서 정해진다.
// DB 연결 문자열은 appsettings.{환경}.json에서 온다 - 개발이면 로컬 Development.db, 운영이면
// 공유폴더의 Production.db다. 즉 개발/운영 전환은 이 파일이 아니라 설정 파일 선택으로 이뤄진다.
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 포털(CleanPotal)과 같은 DB·같은 설정 키를 쓴다. MES 는 포털 안으로 들어가는 중이라
        // DB 가 둘로 갈려 있으면 LOT 과 사원·일정을 한 화면에서 엮을 수 없다.
        // 포털이 쓰는 ConnectionStrings:Default 를 우선 보고, 없으면 예전 키로 물러선다.
        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration.GetConnectionString("ProductionManagementDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default 설정이 필요합니다. 포털과 같은 appsettings.local.json 을 확인하세요.");

        // 공급자도 포털과 같은 키로 고른다(Database:Provider). 기본값은 포털과 동일하게 SQLite —
        // 설정을 안 넣었다고 갑자기 배포가 깨지지 않게 한다.
        var provider = (configuration["Database:Provider"] ?? "Sqlite").Trim();
        var useSqlite = provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
        return services.AddInfrastructure(connectionString, useSqlite);
    }

    /// <summary>
    /// 연결 문자열과 공급자를 호스트가 정해서 넘기는 경우. 포털 안에서 돌 때 이쪽을 쓴다.
    ///
    /// 포털은 설정이 비어 있으면 SQLite 파일 경로를 스스로 만들어 쓴다. 그 값을 그대로 받아야
    /// 포털과 MES 가 같은 DB 를 본다 — 설정만 다시 읽으면 포털은 파일 DB, MES 는 예외로 갈린다.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool useSqlite)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (useSqlite) options.UseSqlite(connectionString);
            else options.UseSqlServer(connectionString);
        });

        services.AddScoped(typeof(IRepository<,>), typeof(EfRepository<,>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILotNumberGenerator, LotNumberGenerator>();
        services.AddScoped<IExportNumberGenerator, ExportNumberGenerator>();
        services.AddScoped<IAuditLogger, EfAuditLogger>();
        services.AddScoped<ILotQueryRepository, EfLotQueryRepository>();
        services.AddScoped<IProcessHistoryQueryRepository, EfProcessHistoryQueryRepository>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<ILotCertificateService, ProductionManagement.Infrastructure.Services.LotCertificateService>();
        // ICertificateExcelFiller(Excel COM 구현)는 net10.0-windows인 Certificate 프로젝트에 있어 Wpf에서
        // 등록한다(App.xaml.cs). 여기서는 데이터 수집 서비스만 등록한다.
        services.AddScoped<ICertificateFillService, ProductionManagement.Infrastructure.Services.CertificateFillService>();
        // 2026-08-28: 아이디/비밀번호 로그인 계정으로 전환. 세션 사용자(로그인 결과)를 보관하는 Singleton을
        // ICurrentUserProvider로 쓴다(기존 WindowsCurrentUserProvider 대체).
        services.AddSingleton<ProductionManagement.Infrastructure.Security.SessionCurrentUserProvider>();
        services.AddSingleton<ICurrentUserProvider>(sp => sp.GetRequiredService<ProductionManagement.Infrastructure.Security.SessionCurrentUserProvider>());
        services.AddSingleton<IPasswordHasher, ProductionManagement.Infrastructure.Security.Pbkdf2PasswordHasher>();
        services.AddScoped<IAuthService, ProductionManagement.Infrastructure.Security.AuthService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();

        return services;
    }
}
