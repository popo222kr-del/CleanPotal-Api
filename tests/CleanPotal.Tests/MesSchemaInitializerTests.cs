using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 포털 DB 안에 MES 표를 만드는 길. 예전에는 Lot 표 하나만 보고 "있으면 끝" 이라 새 MES 표·컬럼이 영영
/// 생기지 않았다. 이제 표마다, 컬럼마다 없는 것만 채우고 여러 번 불러도 같아야 한다.
/// </summary>
public class MesSchemaInitializerTests
{
    private static long Scalar(ApplicationDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task 빠진_MES_표와_컬럼만_채우고_다시_불러도_그대로다()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mes-schema-{Guid.NewGuid():N}.db");
        try
        {
            // 포털 표가 이미 있는 DB — EnsureCreated 는 아무것도 하지 않는 상황.
            using (var portal = new CleanPotalDbContext(new DbContextOptionsBuilder<CleanPotalDbContext>().UseSqlite($"Data Source={file}").Options))
                portal.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={file}"));
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await MesSchemaInitializer.EnsureAsync(db);
            var lotTable = db.Model.FindEntityType(typeof(Lot))!.GetTableName()!;
            var mesTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).Where(n => n is not null).Distinct().Count();
            await db.Database.OpenConnectionAsync();
            Assert.Equal(1, Scalar(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{lotTable}'"));

            // 운영 중에 모델에 컬럼이 새로 생긴 상황을 흉내 낸다 — Lot 표에서 컬럼 하나를 떼어 낸다.
            var column = db.Model.FindEntityType(typeof(Lot))!.GetProperties()
                .First(p => !p.IsPrimaryKey() && !p.IsForeignKey() && !p.IsIndex() && p.IsColumnNullable()).GetColumnBaseName();
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{lotTable}\" DROP COLUMN \"{column}\"");
            Assert.Equal(0, Scalar(db, $"SELECT COUNT(*) FROM pragma_table_info('{lotTable}') WHERE name='{column}'"));

            await MesSchemaInitializer.EnsureAsync(db);
            await MesSchemaInitializer.EnsureAsync(db);

            Assert.Equal(1, Scalar(db, $"SELECT COUNT(*) FROM pragma_table_info('{lotTable}') WHERE name='{column}'"));
            Assert.True(mesTables > 5);
        }
        finally
        {
            SqliteCleanup(file);
        }
    }

    private static void SqliteCleanup(string file)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(file); } catch (IOException) { /* 임시 파일은 남아도 된다 */ }
    }
}
