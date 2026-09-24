using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>이미 운영 중인 DB(인덱스가 없던 때 만든 표)에도 새 조회 인덱스를 덧붙이고, 여러 번 돌려도 안전하다.</summary>
public class SchemaUpgraderIndexTests
{
    private static int IndexCount(TestDb t, string name)
    {
        var conn = t.Db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{name}'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void 없는_인덱스만_만들고_다시_돌려도_그대로다()
    {
        using var t = new TestDb();
        t.Db.Database.ExecuteSqlRaw(@"DROP INDEX ""IX_ZigbeeReadings_IsSnapshot_ReceivedAt""");
        t.Db.Database.ExecuteSqlRaw(@"DROP INDEX ""IX_ShiftSchedules_TargetDate""");

        SchemaUpgrader.Run(t.Db, useSqlite: true);
        SchemaUpgrader.Run(t.Db, useSqlite: true);

        Assert.Equal(1, IndexCount(t, "IX_ZigbeeReadings_IsSnapshot_ReceivedAt"));
        Assert.Equal(1, IndexCount(t, "IX_ShiftSchedules_TargetDate"));
    }
}

/// <summary>표를 만든 뒤 인덱스에서 실패하면 표도 되돌려, 다음 실행 때 표·인덱스를 다시 만든다.</summary>
public class SchemaUpgraderTableTransactionTests
{
    private static int Count(TestDb t, string type, string name)
    {
        var conn = t.Db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='{type}' AND name='{name}'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void 인덱스_실패시_표도_되돌리고_다음_실행에서_다시_만든다()
    {
        using var t = new TestDb();
        t.Db.Database.ExecuteSqlRaw(@"DROP TABLE ""BrokenOptions""");
        // 같은 이름의 인덱스를 다른 표에 미리 만들어 두면 BrokenOptions 인덱스 생성이 실패한다.
        t.Db.Database.ExecuteSqlRaw(@"CREATE TABLE ""Blocker"" (""Id"" INTEGER)");
        t.Db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_BrokenOptions_Kind_Name"" ON ""Blocker"" (""Id"")");

        Assert.ThrowsAny<Exception>(() => SchemaUpgrader.Run(t.Db, useSqlite: true));
        Assert.Equal(0, Count(t, "table", "BrokenOptions"));

        t.Db.Database.ExecuteSqlRaw(@"DROP INDEX ""IX_BrokenOptions_Kind_Name""");
        SchemaUpgrader.Run(t.Db, useSqlite: true);

        Assert.Equal(1, Count(t, "table", "BrokenOptions"));
        Assert.Equal(1, Count(t, "index", "IX_BrokenOptions_Kind_Name"));
    }
}
