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
