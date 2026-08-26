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

    private static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
