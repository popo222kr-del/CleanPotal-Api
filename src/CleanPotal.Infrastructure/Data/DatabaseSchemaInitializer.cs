using System.Text.RegularExpressions;
using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 앱 시작 시 현재 모델의 기본 스키마를 보장하고, 운영 중 추가된 항목만 안전하게 덧붙인다.
/// </summary>
public static class DatabaseSchemaInitializer
{
    private static readonly Regex CreateTablePattern = new(
        "^\\s*CREATE TABLE \\\"(?<name>[^\\\"]+)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CreateIndexPattern = new(
        "^\\s*CREATE (?<unique>UNIQUE )?INDEX \\\"(?<name>[^\\\"]+)\\\" ON \\\"(?<table>[^\\\"]+)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 빈 DB는 현재 모델로 만들고, 기존 DB는 <see cref="SchemaUpgrader"/>가 없는 항목만 추가한다.
    ///
    /// SQLite 운영 DB는 WPF 시절부터 이어져 EF 마이그레이션 이력과 실제 스키마가 일치하지
    /// 않을 수 있다. 여기서 Migrate()를 실행하면 이미 존재하는 테이블을 다시 만들다 앱이
    /// 시작되지 않으므로, 런타임 자동 마이그레이션과 기존 DB를 섞지 않는다.
    /// </summary>
    public static void Prepare(CleanPotalDbContext db, bool useSqlite)
    {
        db.Database.EnsureCreated();

        // EnsureCreated는 표가 하나라도 있는 SQLite DB에서는 아무 일도 하지 않는다.
        // WPF DB처럼 일부 표만 있는 경우 현재 EF 모델의 CREATE 문에서 빠진 표만 골라 만든다.
        // 기존 표에는 CREATE를 실행하지 않으므로 자료와 기존 구조는 그대로 보존된다.
        if (useSqlite)
            EnsureMissingSqliteTables(db);

        // 업무상 의미가 있는 기본값(예: AccessMes=1, IsActive=1)은 먼저 명시적 규칙으로 채운다.
        SchemaUpgrader.Run(db, useSqlite);

        // 명시 목록에 없던 레거시 컬럼도 현재 모델과 비교해서 추가한다. 일부 마이그레이션만
        // 적용된 DB에서 누락 컬럼이 하나씩 뒤늦게 발견되어 기동이 반복 실패하는 것을 막는다.
        if (useSqlite)
        {
            EnsureMissingSqliteColumns(db);
            RebuildLegacyInventoryTable(db);
        }
    }

    private static void EnsureMissingSqliteTables(CleanPotalDbContext db)
    {
        var statements = db.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statements)
        {
            var match = CreateTablePattern.Match(statement);
            if (!match.Success) continue;

            var table = match.Groups["name"].Value;
            if (SqliteTableExists(db, table)) continue;

            var createIfMissing = CreateTablePattern.Replace(
                statement,
                $"CREATE TABLE IF NOT EXISTS \"{table.Replace("\"", "\"\"")}\"",
                1);
            db.Database.ExecuteSqlRaw(createIfMissing);
            createdTables.Add(table);
            Console.WriteLine($"[schema] {table} 누락 테이블 생성(현재 모델 기준)");
        }

        // 방금 만든 표의 인덱스만 만든다. 기존 레거시 표의 인덱스는 현재 운영 구조를 유지한다.
        foreach (var statement in statements)
        {
            var match = CreateIndexPattern.Match(statement);
            if (!match.Success || !createdTables.Contains(match.Groups["table"].Value)) continue;

            var prefix = match.Groups["unique"].Success
                ? "CREATE UNIQUE INDEX IF NOT EXISTS "
                : "CREATE INDEX IF NOT EXISTS ";
            var createIfMissing = CreateIndexPattern.Replace(
                statement,
                $"{prefix}\"{match.Groups["name"].Value.Replace("\"", "\"\"")}\" ON \"{match.Groups["table"].Value.Replace("\"", "\"\"")}\"",
                1);
            db.Database.ExecuteSqlRaw(createIfMissing);
        }

        if (createdTables.Count > 0)
            Console.WriteLine($"[schema] 누락 테이블 {createdTables.Count}개 생성 완료 (기존 데이터는 변경하지 않음).");
    }

    private static bool SqliteTableExists(CleanPotalDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void EnsureMissingSqliteColumns(CleanPotalDbContext db)
    {
        var added = 0;

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null || !SqliteTableExists(db, table)) continue;

            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName(storeObject);
                if (column is null || SqliteColumnExists(db, table, column)) continue;

                if (property.IsPrimaryKey())
                    throw new InvalidOperationException(
                        $"[schema] 기존 {table} 테이블에 기본키 컬럼 {column}이 없습니다. 자동 추가할 수 없습니다.");

                var storeType = property.GetRelationalTypeMapping().StoreType;
                var nullability = property.IsColumnNullable(storeObject)
                    ? "NULL"
                    : $"NOT NULL DEFAULT {SqliteDefaultLiteral(property.ClrType)}";
                var sql = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {storeType} {nullability}";
                db.Database.ExecuteSqlRaw(sql);
                Console.WriteLine($"[schema] {table}.{column} 누락 컬럼 추가(현재 모델 기준)");
                added++;
            }
        }

        if (added > 0)
            Console.WriteLine($"[schema] 누락 컬럼 {added}개 생성 완료 (기존 데이터는 기본값으로 보존).");
    }

    private static bool SqliteColumnExists(CleanPotalDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info({QuoteLiteral(table)}) WHERE name = $column";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$column";
            parameter.Value = column;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static string SqliteDefaultLiteral(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string) || type == typeof(char)) return "''";
        if (type == typeof(bool) || type.IsEnum || IsNumeric(type)) return "0";
        if (type == typeof(DateTime)) return "'0001-01-01 00:00:00'";
        if (type == typeof(DateOnly)) return "'0001-01-01'";
        if (type == typeof(TimeOnly) || type == typeof(TimeSpan)) return "'00:00:00'";
        if (type == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
        if (type == typeof(byte[])) return "X''";

        throw new InvalidOperationException($"[schema] {type.Name} 형식의 안전한 SQLite 기본값을 정할 수 없습니다.");
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string QuoteLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private static void RebuildLegacyInventoryTable(CleanPotalDbContext db)
    {
        const string table = "InventoryItems";
        const string legacyMarker = "PreviousStock";
        const string tempTable = "__InventoryItems_Current";
        const string backupTable = "InventoryItems_LegacyBackup";

        if (!SqliteColumnExists(db, table, legacyMarker)) return;

        var entityType = db.Model.FindEntityType(typeof(InventoryItem))
            ?? throw new InvalidOperationException("[schema] InventoryItem 모델을 찾을 수 없습니다.");
        var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var properties = entityType.GetProperties()
            .Select(property => new
            {
                Property = property,
                Column = property.GetColumnName(storeObject)
            })
            .Where(item => item.Column is not null)
            .ToList();

        var createStatement = db.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First(statement =>
            {
                var match = CreateTablePattern.Match(statement);
                return match.Success && match.Groups["name"].Value.Equals(table, StringComparison.OrdinalIgnoreCase);
            });
        var createTempStatement = CreateTablePattern.Replace(
            createStatement,
            $"CREATE TABLE {Quote(tempTable)}",
            1);

        var destinationColumns = string.Join(", ", properties.Select(item => Quote(item.Column!)));
        var sourceValues = string.Join(", ", properties.Select(item =>
            LegacyInventoryValueSql(db, item.Property, item.Column!)));

        using var transaction = db.Database.BeginTransaction();
        try
        {
            var dropTempSql = $"DROP TABLE IF EXISTS {Quote(tempTable)}";
            var copyRowsSql =
                $"INSERT INTO {Quote(tempTable)} ({destinationColumns}) SELECT {sourceValues} FROM {Quote(table)}";
            var createBackupSql = $"CREATE TABLE {Quote(backupTable)} AS SELECT * FROM {Quote(table)}";
            var dropLegacySql = $"DROP TABLE {Quote(table)}";
            var renameTempSql = $"ALTER TABLE {Quote(tempTable)} RENAME TO {Quote(table)}";

            db.Database.ExecuteSqlRaw(dropTempSql);
            db.Database.ExecuteSqlRaw(createTempStatement);
            db.Database.ExecuteSqlRaw(copyRowsSql);

            // 변환 전 원본을 별도 표로 한 번 보관한다. 앱은 이 표를 사용하지 않지만 문제가 생기면
            // PreviousStock·ExpectedDate·Note를 포함한 원래 행을 그대로 확인할 수 있다.
            if (!SqliteTableExists(db, backupTable))
                db.Database.ExecuteSqlRaw(createBackupSql);

            db.Database.ExecuteSqlRaw(dropLegacySql);
            db.Database.ExecuteSqlRaw(renameTempSql);
            transaction.Commit();
            Console.WriteLine(
                $"[schema] {table} 구형 구조 변환 완료({legacyMarker} 제거, 원본은 {backupTable}에 보관).");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string LegacyInventoryValueSql(
        CleanPotalDbContext db,
        Microsoft.EntityFrameworkCore.Metadata.IProperty property,
        string column)
    {
        var nullable = property.IsNullable;
        var fallback = nullable ? "NULL" : SqliteDefaultLiteral(property.ClrType);
        var currentExists = SqliteColumnExists(db, "InventoryItems", column);

        if (column.Equals("ExpectedReceipt", StringComparison.OrdinalIgnoreCase)
            && SqliteColumnExists(db, "InventoryItems", "ExpectedDate"))
        {
            var current = currentExists ? $"NULLIF(CAST({Quote(column)} AS TEXT), '')" : "NULL";
            return $"COALESCE({current}, CAST({Quote("ExpectedDate")} AS TEXT), '')";
        }

        if (column.Equals("Memo", StringComparison.OrdinalIgnoreCase)
            && SqliteColumnExists(db, "InventoryItems", "Note"))
        {
            var current = currentExists ? $"NULLIF(CAST({Quote(column)} AS TEXT), '')" : "NULL";
            return $"COALESCE({current}, CAST({Quote("Note")} AS TEXT), '')";
        }

        if (column.Equals("OrderNo", StringComparison.OrdinalIgnoreCase) && currentExists)
            return $"CASE WHEN COALESCE({Quote(column)}, 0) = 0 THEN {Quote("Id")} ELSE {Quote(column)} END";

        return currentExists
            ? nullable ? Quote(column) : $"COALESCE({Quote(column)}, {fallback})"
            : fallback;
    }
}
