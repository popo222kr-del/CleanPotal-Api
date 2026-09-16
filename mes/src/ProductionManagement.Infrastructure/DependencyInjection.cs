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
        var connectionString = configuration.GetConnectionString("ProductionManagementDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ProductionManagementDb 설정이 필요합니다. appsettings.{Environment}.json을 확인하세요.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped(typeof(IRepository<,>), typeof(EfRepository<,>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILotNumberGenerator, SqliteLotNumberGenerator>();
        services.AddScoped<IExportNumberGenerator, SqliteExportNumberGenerator>();
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
