using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CleanPotal.Infrastructure.Data;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// DB 백업 — `dotnet CleanPotal.Api.dll backup-db [--dir 폴더]`
///
/// SQL Server: BACKUP DATABASE 로 전체 백업 파일(.bak)을 만들고 RESTORE VERIFYONLY 로 읽히는지 확인한다.
///   파일은 **DB 서버 PC 의 디스크**에 생긴다(SQL Server 가 직접 쓴다). 폴더를 안 주면 SQL Server 기본 백업 폴더.
///   이름은 요일별(JUEON_Mon.bak …)로 덮어써서 지우는 작업 없이 최근 7일치가 남는다.
///   매월 1일 것은 월별 이름(JUEON_2026-10.bak)으로 따로 남긴다 — 요일 파일이 망가져도 한 달 전으로는 돌아갈 수 있게.
/// SQLite(개발): App_Data/backup 에 VACUUM INTO 로 복사본을 만든다.
///
/// 비밀번호·연결 문자열은 출력하지 않는다. 실패하면 종료 코드 1(예약 작업 기록에 남는다).
/// </summary>
public static class DbBackup
{
    /// <summary>요일 파일 이름 조각 — 예약 작업이 매일 같은 7개 파일을 돌려 쓴다.</summary>
    public static string DaySlot(DateTime now) => now.DayOfWeek.ToString()[..3];

    /// <summary>백업 파일 이름들(요일 + 매월 1일이면 월별). 폴더 구분자는 DB 서버(윈도) 기준 \.</summary>
    public static IReadOnlyList<string> FileNames(string dbName, DateTime now)
    {
        var names = new List<string> { $"{dbName}_{DaySlot(now)}.bak" };
        if (now.Day == 1) names.Add($"{dbName}_{now:yyyy-MM}.bak");
        return names;
    }

    public static int Run(CleanPotalDbContext db, bool useSqlite, string contentRoot, string[] args)
    {
        var dirArg = ArgValue(args, "--dir");
        try
        {
            return useSqlite ? RunSqlite(db, contentRoot, dirArg) : RunSqlServer(db, dirArg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[backup][오류] 백업 실패: {ex.Message}");
            if (ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("권한"))
                Console.WriteLine("[backup]   → 포털 DB 계정에 백업 권한이 없습니다. DB 관리자에게 db_backupoperator(또는 db_owner) 권한을 요청하세요.");
            if (ex.Message.Contains("Operating system error", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("운영 체제 오류"))
                Console.WriteLine("[backup]   → DB 서버의 SQL Server 서비스 계정이 그 폴더에 쓸 수 없습니다. --dir 로 DB 서버 안의 다른 폴더를 주세요.");
            return 1;
        }
    }

    private static int RunSqlServer(CleanPotalDbContext db, string? dirArg)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        var conn = (SqlConnection)db.Database.GetDbConnection();
        conn.Open();
        var dbName = conn.Database;

        var dir = dirArg ?? Scalar(conn, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))");
        if (string.IsNullOrWhiteSpace(dir))
        {
            Console.WriteLine("[backup][오류] SQL Server 기본 백업 폴더를 알 수 없습니다. --dir \"D:\\Backup\" 처럼 DB 서버 안의 폴더를 주세요.");
            return 1;
        }
        dir = dir.TrimEnd('\\', '/');

        var dataMb = Scalar(conn, "SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024 AS nvarchar(40)) FROM sys.database_files WHERE type = 0");
        Console.WriteLine($"[backup] DB {dbName} — 데이터 파일 {dataMb} MB (SQL Server Express 한도 10240 MB)");

        foreach (var name in FileNames(dbName, DateTime.Now))
        {
            var path = $"{dir}\\{name}";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Exec(conn, "BACKUP DATABASE @db TO DISK = @path WITH INIT, FORMAT, CHECKSUM, NAME = @label",
                ("@db", dbName), ("@path", path), ("@label", $"CleanPotal {DateTime.Now:yyyy-MM-dd HH:mm}"));
            Exec(conn, "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM", ("@path", path));
            Console.WriteLine($"[backup] ✅ {path} (DB 서버 PC) — {sw.Elapsed.TotalSeconds:0}초, 읽기 확인됨");
        }
        Console.WriteLine("[backup] 요일별 7개 파일이 돌아가며 덮어써진다. DB 서버 PC 가 고장 나면 같이 잃으므로 그 폴더를 NAS 로도 복사해 두세요.");
        return 0;
    }

    private static int RunSqlite(CleanPotalDbContext db, string contentRoot, string? dirArg)
    {
        var dir = dirArg ?? Path.Combine(contentRoot, "App_Data", "backup");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"cleanpotal_{DaySlot(DateTime.Now)}.db");
        if (File.Exists(path)) File.Delete(path);   // VACUUM INTO 는 있는 파일에 쓰지 않는다
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "VACUUM INTO $path";
            var p = cmd.CreateParameter(); p.ParameterName = "$path"; p.Value = path; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }
        Console.WriteLine($"[backup] ✅ {path} ({new FileInfo(path).Length / 1024} KB)");
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? Scalar(SqlConnection conn, string sql)
    {
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        return cmd.ExecuteScalar() as string;
    }

    private static void Exec(SqlConnection conn, string sql, params (string Name, string Value)[] ps)
    {
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 1800 };
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}
