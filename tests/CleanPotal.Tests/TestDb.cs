using CleanPotal.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Tests;

/// <summary>
/// 테스트용 SQLite 인메모리 DB. 실제 관계형 공급자를 쓰므로 고유 인덱스·FK 같은
/// 제약도 그대로 검증된다(InMemory 공급자는 이걸 무시한다).
/// 연결을 열어둔 동안만 DB가 살아 있으므로 Dispose 에서 함께 닫는다.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public CleanPotalDbContext Db { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<CleanPotalDbContext>()
            .UseSqlite(_conn)
            .Options;
        Db = new CleanPotalDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>같은 DB를 보는 별도 컨텍스트(변경 추적 캐시 없이 다시 읽을 때 사용).</summary>
    public CleanPotalDbContext NewContext()
        => new(new DbContextOptionsBuilder<CleanPotalDbContext>().UseSqlite(_conn).Options);

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}
