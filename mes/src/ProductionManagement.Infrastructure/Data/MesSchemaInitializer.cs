using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProductionManagement.Infrastructure.Data;

/// <summary>
/// MES 테이블을 포털(CleanPotal) DB 안에 만든다.
///
/// 왜 EnsureCreated 만으로 안 되나: EnsureCreated 는 "테이블이 하나도 없을 때만" 동작한다.
/// 포털 DB 에는 이미 30개 넘는 테이블이 있어서 아무 일도 하지 않고 지나간다.
///
/// 왜 마이그레이션이 아닌가: 기존 마이그레이션 33개는 SQLite 전용인 데다 테이블 이름에
/// Mes 접두사가 붙기 전에 만들어져 더 이상 모델과 맞지 않는다. 포털도 마이그레이션 대신
/// EnsureCreated + SchemaUpgrader 로 가고 있어 방식을 맞춘다.
///
/// 안전 원칙 — <b>추가만</b> 한다. 표마다 없으면 만들고, 있는 표에는 빠진 컬럼만 덧붙인다.
/// DROP 도, 컬럼 변경도 하지 않는다.
///
/// 예전 방식의 문제: Lot 표 하나만 보고 "있으면 끝" 이라서 새 MES 표·컬럼이 영영 생기지 않았고,
/// 존재 확인이 모든 예외를 삼켜 false 를 돌려줘 일시 오류 한 번에 전체 생성 스크립트를 다시 돌리다
/// "already an object named" 로 서버가 뜨지 않을 수 있었다. 이제는 카탈로그를 직접 조회한다.
/// </summary>
public static class MesSchemaInitializer
{
    private static readonly Regex CreateTablePattern = new(
        @"^\s*CREATE TABLE [\[""](?<name>[^\]""]+)[\]""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CreateIndexPattern = new(
        @"^\s*CREATE (UNIQUE )?INDEX [\[""][^\]""]+[\]""] ON [\[""](?<table>[^\]""]+)[\]""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GoSeparator = new(
        @"^\s*GO\s*$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static async Task EnsureAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        // SQLite 파일처럼 DB 자체가 없을 수 있다 — 먼저 만들어 둔다(빈 DB 면 여기서 모두 만들어진다).
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var sqlServer = db.Database.IsSqlServer();
        var statements = SplitStatements(db.Database.GenerateCreateScript(), sqlServer);
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // EF 가 이 DbContext 의 모델(=MES 표만)로 만든 스크립트에서 없는 표만 골라 만든다.
        foreach (var statement in statements)
        {
            var match = CreateTablePattern.Match(statement);
            if (!match.Success) continue;
            var table = match.Groups["name"].Value;
            if (await TableExistsAsync(db, table, sqlServer, cancellationToken)) continue;

            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            created.Add(table);
            Console.WriteLine($"[mes][schema] {table} 표를 만들었습니다.");
        }

        // 방금 만든 표의 인덱스만 만든다. 기존 표의 인덱스 구성은 건드리지 않는다.
        foreach (var statement in statements)
        {
            var match = CreateIndexPattern.Match(statement);
            if (match.Success && created.Contains(match.Groups["table"].Value))
                await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        // 이미 있던 MES 표에는 모델에 새로 생긴 컬럼만 덧붙인다.
        var added = 0;
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null || created.Contains(table)) continue;
            if (!await TableExistsAsync(db, table, sqlServer, cancellationToken)) continue;

            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName(storeObject);
                if (column is null || property.IsPrimaryKey()) continue;
                if (await ColumnExistsAsync(db, table, column, sqlServer, cancellationToken)) continue;

                var sql = AddColumnSql(table, column, property.GetRelationalTypeMapping().StoreType,
                    property.IsColumnNullable(storeObject), property.ClrType, sqlServer);
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                    Console.WriteLine($"[mes][schema] {table}.{column} 컬럼을 덧붙였습니다.");
                    added++;
                }
                catch (Exception ex)
                {
                    // 컬럼 하나 때문에 포털 전체가 뜨지 않으면 안 된다 — 남기고 넘어간다.
                    Console.WriteLine($"[mes][schema][경고] {table}.{column} 컬럼 추가 실패: {ex.Message}");
                }
            }
        }

        if (created.Count > 0 || added > 0)
            Console.WriteLine($"[mes][schema] 표 {created.Count}개, 컬럼 {added}개를 추가했습니다(기존 데이터는 그대로).");
    }

    public static string AddColumnSql(string table, string column, string storeType, bool nullable, Type clrType, bool sqlServer)
    {
        string Q(string id) => sqlServer ? $"[{id.Replace("]", "]]")}]" : $"\"{id.Replace("\"", "\"\"")}\"";
        var head = sqlServer
            ? $"ALTER TABLE {Q(table)} ADD {Q(column)} {storeType}"
            : $"ALTER TABLE {Q(table)} ADD COLUMN {Q(column)} {storeType}";
        if (nullable) return head + " NULL";
        var literal = DefaultLiteral(clrType, storeType, sqlServer);
        return sqlServer
            ? $"{head} NOT NULL CONSTRAINT {Q($"DF_{table}_{column}")} DEFAULT {literal}"
            : $"{head} NOT NULL DEFAULT {literal}";
    }

    private static string DefaultLiteral(Type clrType, string storeType, bool sqlServer)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string) || type == typeof(char)) return sqlServer ? "N''" : "''";
        if (type == typeof(bool) || type.IsEnum || IsNumeric(type)) return "0";
        if (type == typeof(DateTime))
            return !sqlServer ? "'0001-01-01 00:00:00'"
                : storeType.StartsWith("datetime2", StringComparison.OrdinalIgnoreCase) ? "'0001-01-01T00:00:00'" : "'1900-01-01T00:00:00'";
        if (type == typeof(DateTimeOffset)) return "'0001-01-01T00:00:00+00:00'";
        if (type == typeof(DateOnly)) return "'0001-01-01'";
        if (type == typeof(TimeOnly) || type == typeof(TimeSpan)) return "'00:00:00'";
        if (type == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
        if (type == typeof(byte[])) return sqlServer ? "0x" : "X''";
        throw new InvalidOperationException($"[mes][schema] {type.Name} 형식의 안전한 기본값을 정할 수 없습니다.");
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static List<string> SplitStatements(string script, bool sqlServer)
    {
        var parts = sqlServer ? GoSeparator.Split(script) : script.Split(';');
        return parts.Select(p => p.Trim().TrimEnd(';').Trim()).Where(p => p.Length > 0).ToList();
    }

    private static Task<bool> TableExistsAsync(ApplicationDbContext db, string table, bool sqlServer, CancellationToken ct)
        => ScalarAsync(db, sqlServer
            ? "SELECT COUNT(*) FROM sys.tables WHERE name = @p"
            : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @p", table, null, ct);

    private static Task<bool> ColumnExistsAsync(ApplicationDbContext db, string table, string column, bool sqlServer, CancellationToken ct)
        => ScalarAsync(db, sqlServer
            ? "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = @p AND c.name = @q"
            : "SELECT COUNT(*) FROM pragma_table_info(@p) WHERE name = @q", table, column, ct);

    private static async Task<bool> ScalarAsync(ApplicationDbContext db, string sql, string p, string? q, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var p1 = command.CreateParameter(); p1.ParameterName = "@p"; p1.Value = p; command.Parameters.Add(p1);
            if (q is not null)
            {
                var p2 = command.CreateParameter(); p2.ParameterName = "@q"; p2.Value = q; command.Parameters.Add(p2);
            }
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct) ?? 0) > 0;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
