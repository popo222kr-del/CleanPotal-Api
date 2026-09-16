using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProductionManagement.Infrastructure.Data;

// `dotnet ef migrations add` 같은 design-time 도구가 앱을 전체 기동하지 않고도 DbContext를 만들 수 있게 해준다.
// 여기서 쓰는 연결 문자열은 Migration 생성/설계 전용이며, 실제 런타임 연결 문자열은 appsettings.{Environment}.json이 담당한다.
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlite("Data Source=Development.db;Default Timeout=30");
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
