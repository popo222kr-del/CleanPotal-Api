using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

public class DatabaseSchemaInitializerTests
{
    [Fact]
    public void Prepare_WhenSqliteHasOnlyPartOfTheModel_RecreatesMissingTablesAndPreservesData()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CleanPotalDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var setup = new CleanPotalDbContext(options))
        {
            // 현재 모델로 표를 만든 뒤 ScheduleBlocks만 남겨, 운영 장애와 같은 부분 스키마를 만든다.
            setup.Database.EnsureCreated();
            setup.ScheduleBlocks.Add(new ScheduleBlock
            {
                BoardDate = "2026-09-21",
                EquipmentIndex = 1,
                StartMinute = 60,
                RecipeText = "existing-production-data"
            });
            setup.SaveChanges();

            DropEveryApplicationTableExcept(connection, "ScheduleBlocks");
        }

        using (var prepare = new CleanPotalDbContext(options))
        {
            DatabaseSchemaInitializer.Prepare(prepare, useSqlite: true);
            DbSeeder.SeedBase(prepare);
        }

        using var verify = new CleanPotalDbContext(options);
        Assert.False(TableExists(connection, "__EFMigrationsHistory"));
        Assert.Equal("existing-production-data", verify.ScheduleBlocks.Single().RecipeText);

        var expectedTables = verify.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        Assert.All(expectedTables, table => Assert.True(TableExists(connection, table!), $"누락 테이블: {table}"));
    }

    [Fact]
    public void Prepare_WhenLegacyInventoryHasRemovedRequiredColumns_RebuildsAndPreservesRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CleanPotalDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var setup = new CleanPotalDbContext(options))
        {
            setup.Database.EnsureCreated();
            setup.InventoryItems.Add(new InventoryItem
            {
                OrderNo = 1,
                ItemName = "기존 재고",
                StorageLocation = "창고",
                ExpectedReceipt = "삭제 전 값",
                Memo = "현재 메모"
            });
            setup.SaveChanges();
            setup.Database.ExecuteSqlRaw("ALTER TABLE \"InventoryItems\" DROP COLUMN \"ExpectedReceipt\"");
            setup.Database.ExecuteSqlRaw("ALTER TABLE \"InventoryItems\" DROP COLUMN \"Memo\"");
            setup.Database.ExecuteSqlRaw("ALTER TABLE \"InventoryItems\" ADD COLUMN \"PreviousStock\" REAL NOT NULL DEFAULT 0");
            setup.Database.ExecuteSqlRaw("ALTER TABLE \"InventoryItems\" ADD COLUMN \"ExpectedDate\" TEXT NULL");
            setup.Database.ExecuteSqlRaw("ALTER TABLE \"InventoryItems\" ADD COLUMN \"Note\" TEXT NOT NULL DEFAULT ''");
            setup.Database.ExecuteSqlRaw(
                "UPDATE \"InventoryItems\" SET \"PreviousStock\" = 7, \"ExpectedDate\" = '2026-09-30', \"Note\" = '구형 메모'");
        }

        using (var prepare = new CleanPotalDbContext(options))
            DatabaseSchemaInitializer.Prepare(prepare, useSqlite: true);

        using var verify = new CleanPotalDbContext(options);
        var preserved = verify.InventoryItems.Single();
        Assert.Equal("기존 재고", preserved.ItemName);
        Assert.Equal("2026-09-30", preserved.ExpectedReceipt);
        Assert.Equal("구형 메모", preserved.Memo);
        Assert.True(ColumnExists(connection, "InventoryItems", "ExpectedReceipt"));
        Assert.False(ColumnExists(connection, "InventoryItems", "PreviousStock"));
        Assert.True(TableExists(connection, "InventoryItems_LegacyBackup"));
    }

    private static void DropEveryApplicationTableExcept(SqliteConnection connection, string preservedTable)
    {
        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = OFF";
            foreignKeys.ExecuteNonQuery();
        }

        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> $preserved";
            list.Parameters.AddWithValue("$preserved", preservedTable);
            using var reader = list.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE \"{table.Replace("\"", "\"\"")}\"";
            drop.ExecuteNonQuery();
        }

        using var restoreForeignKeys = connection.CreateCommand();
        restoreForeignKeys.CommandText = "PRAGMA foreign_keys = ON";
        restoreForeignKeys.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName.Replace("'", "''")}') WHERE name = $name";
        command.Parameters.AddWithValue("$name", columnName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
