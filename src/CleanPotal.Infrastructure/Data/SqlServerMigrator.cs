using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 기존 SQLite(cleanpotal.db)의 전체 데이터를 현재 연결된 SQL Server DB로 한 번에 복사한다.
/// - 스키마는 호출 전 EnsureCreated()로 이미 만들어져 있다고 가정한다(테이블 45종).
/// - SQLite에서 EF로 읽어(문자열 날짜→DateTime, 0/1→bool 등 타입 변환을 EF가 처리),
///   같은 이름의 SQL Server 테이블로 SqlBulkCopy(KeepIdentity)로 넣어 PK(Id)를 그대로 보존한다.
/// - 복사 중에는 FK 제약을 잠시 꺼서 테이블 순서에 상관없이 넣고, 끝나면 다시 켜서 검증한다.
/// 사용: dotnet run -- migrate-to-sqlserver "C:\경로\cleanpotal.db"
/// </summary>
public static class SqlServerMigrator
{
    public static void CopyFromSqlite(CleanPotalDbContext target, string sqlitePath)
    {
        if (!File.Exists(sqlitePath))
        {
            Console.WriteLine($"[migrate] 원본 SQLite 파일을 찾을 수 없습니다: {sqlitePath}");
            return;
        }

        // 대상 DB가 이미 채워져 있으면 중단(중복/PK 충돌 방지). 빈 DB에서만 실행.
        if (target.Users.Any())
        {
            Console.WriteLine("[migrate] 대상 SQL Server DB에 이미 사용자 데이터가 있습니다. 빈 DB에서만 실행하세요. 중단합니다.");
            return;
        }

        var srcOptions = new DbContextOptionsBuilder<CleanPotalDbContext>()
            .UseSqlite($"Data Source={sqlitePath}")
            .Options;
        using var source = new CleanPotalDbContext(srcOptions);
        source.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var conn = (SqlConnection)target.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) conn.Open();

        var entityTypes = target.Model.GetEntityTypes()
            .Where(t => t.GetTableName() != null)
            .ToList();

        // 1) FK 제약 끄기 (테이블 순서 무관하게 삽입)
        foreach (var et in entityTypes)
            Exec(conn, $"ALTER TABLE [{et.GetSchema() ?? "dbo"}].[{et.GetTableName()}] NOCHECK CONSTRAINT ALL");

        var setMethod = typeof(DbContext).GetMethods()
            .First(m => m.Name == "Set" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

        var report = new List<(string table, int copied)>();

        // 2) 테이블별 복사
        foreach (var et in entityTypes)
        {
          try
          {
            var clr = et.ClrType;
            var table = et.GetTableName()!;
            var schema = et.GetSchema() ?? "dbo";
            var store = StoreObjectIdentifier.Table(table, et.GetSchema());

            // 매핑 대상 컬럼(스칼라 속성만, CLR 프로퍼티가 있는 것) 수집
            var cols = new List<(IProperty prop, PropertyInfo pi, string col, Type colType)>();
            foreach (var p in et.GetProperties())
            {
                if (p.IsShadowProperty()) continue;
                var pi = clr.GetProperty(p.Name);
                if (pi == null) continue;
                var col = p.GetColumnName(store);
                if (string.IsNullOrEmpty(col)) continue;
                var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                if (t.IsEnum) t = Enum.GetUnderlyingType(t);
                cols.Add((p, pi, col!, t));
            }

            var dt = new DataTable();
            foreach (var c in cols) dt.Columns.Add(c.col, c.colType);

            var setObj = (System.Collections.IEnumerable)setMethod.MakeGenericMethod(clr).Invoke(source, null)!;
            foreach (var entity in setObj)
            {
                var row = dt.NewRow();
                foreach (var c in cols)
                {
                    var val = c.pi.GetValue(entity);
                    if (val == null) { row[c.col] = DBNull.Value; continue; }
                    if (val.GetType().IsEnum) val = Convert.ChangeType(val, c.colType);
                    row[c.col] = val;
                }
                dt.Rows.Add(row);
            }

            if (dt.Rows.Count == 0) { report.Add((table, 0)); continue; }

            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity, null)
            {
                DestinationTableName = $"[{schema}].[{table}]",
                BulkCopyTimeout = 0,
            };
            foreach (var c in cols) bulk.ColumnMappings.Add(c.col, c.col);
            bulk.WriteToServer(dt);
            report.Add((table, dt.Rows.Count));
            Console.WriteLine($"[migrate] {table,-28} {dt.Rows.Count,6} 행 복사");
          }
          catch (Exception ex)
          {
              Console.WriteLine($"[migrate][건너뜀] {et.GetTableName()} : {ex.Message}");
          }
        }

        // 3) FK 제약 다시 켜기(WITH CHECK 로 무결성 검증)
        foreach (var et in entityTypes)
            Exec(conn, $"ALTER TABLE [{et.GetSchema() ?? "dbo"}].[{et.GetTableName()}] WITH CHECK CHECK CONSTRAINT ALL");

        var total = report.Sum(r => r.copied);
        Console.WriteLine($"[migrate] 완료: 테이블 {report.Count}종, 총 {total} 행 복사됨.");
        Console.WriteLine("[migrate] 원본(SQLite) 대비 행수를 확인하세요:");
        foreach (var (tbl, copied) in report.Where(r => r.copied > 0))
            Console.WriteLine($"          {tbl,-28} {copied,6}");
    }

    /// <summary>
    /// 현재 연결된 SQL Server DB의 테이블 데이터를 삭제한다(스키마·테이블은 유지).
    /// WPF 데이터 재임포트(refresh-from-wpf) 전에 "빈 상태"로 되돌리는 용도.
    /// FK 제약을 잠시 꺼서 순서 상관없이 DELETE 하고, 끝나면 다시 켠다.
    /// preserveTypes 로 지정한 엔티티(웹에서 관리하는 계정·권한·부서 등)는 비우지 않는다.
    /// </summary>
    public static void ClearAllTables(CleanPotalDbContext target, params Type[] preserveTypes)
    {
        var conn = (SqlConnection)target.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) conn.Open();

        var preserve = new HashSet<Type>(preserveTypes ?? Array.Empty<Type>());
        var allTypes = target.Model.GetEntityTypes()
            .Where(t => t.GetTableName() != null)
            .ToList();
        // 제약 on/off 는 전체 테이블에, DELETE 는 보존 대상 제외 테이블에만
        var entityTypes = allTypes.Where(t => !preserve.Contains(t.ClrType)).ToList();

        // FK 제약은 전체 테이블에 끈다(보존 테이블이 비우는 테이블을 참조해도 삭제되게)
        foreach (var et in allTypes)
            Exec(conn, $"ALTER TABLE [{et.GetSchema() ?? "dbo"}].[{et.GetTableName()}] NOCHECK CONSTRAINT ALL");

        foreach (var et in entityTypes)
        {
            var schema = et.GetSchema() ?? "dbo";
            var table = et.GetTableName()!;
            Exec(conn, $"DELETE FROM [{schema}].[{table}]");
            // IDENTITY 컬럼이 있으면 다음 Id가 1부터 시작하도록 되돌린다(없으면 무시).
            try { Exec(conn, $"DBCC CHECKIDENT('[{schema}].[{table}]', RESEED, 0)"); }
            catch { /* IDENTITY 없는 테이블 */ }
        }

        foreach (var et in allTypes)
            Exec(conn, $"ALTER TABLE [{et.GetSchema() ?? "dbo"}].[{et.GetTableName()}] WITH CHECK CHECK CONSTRAINT ALL");

        var kept = allTypes.Count - entityTypes.Count;
        Console.WriteLine($"[refresh] 테이블 {entityTypes.Count}종을 비웠습니다(보존 {kept}종: 계정·권한·부서·기준정보).");
    }

    /// <summary>
    /// 현재 SQL Server DB의 모든 테이블을 삭제한다(FK 먼저 제거 후 테이블 DROP).
    /// 예전에 비정상적으로 만들어진 스키마 잔재([Content] 컬럼 등)를 없애고,
    /// 이후 EnsureCreated 가 현재 모델대로 45개 테이블을 새로 만들게 하기 위함.
    /// DB 자체는 삭제하지 않으므로 DROP DATABASE 권한이 없어도 db_owner 면 동작한다.
    /// </summary>
    public static void DropAllTables(CleanPotalDbContext target)
    {
        var conn = (SqlConnection)target.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) conn.Open();

        // 1) 모든 외래 키 제거
        Exec(conn, @"DECLARE @sql nvarchar(max)=N'';
SELECT @sql += 'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + ' DROP CONSTRAINT ' + QUOTENAME(name) + ';'
FROM sys.foreign_keys;
IF @sql <> N'' EXEC sp_executesql @sql;");

        // 2) 모든 테이블 제거
        Exec(conn, @"DECLARE @sql nvarchar(max)=N'';
SELECT @sql += 'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';'
FROM sys.tables;
IF @sql <> N'' EXEC sp_executesql @sql;");

        Console.WriteLine("[rebuild] 기존 테이블을 모두 삭제했습니다(스키마 초기화).");
    }

    private static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
