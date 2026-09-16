using Microsoft.EntityFrameworkCore;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data;

/// <summary>
/// MES 테이블을 포털(CleanPotal) DB 안에 만든다.
///
/// 왜 EnsureCreated 가 아닌가: EnsureCreated 는 "테이블이 하나도 없을 때만" 동작한다.
/// 포털 DB 에는 이미 30개 넘는 테이블이 있어서 아무 일도 하지 않고 지나가고,
/// MES 테이블은 영영 생기지 않는다.
///
/// 왜 마이그레이션이 아닌가: 기존 마이그레이션 33개는 SQLite 전용인 데다 테이블 이름에
/// Mes 접두사가 붙기 전에 만들어져 더 이상 모델과 맞지 않는다. 포털도 마이그레이션 대신
/// EnsureCreated + SchemaUpgrader 로 가고 있어 방식을 맞춘다.
///
/// 안전 원칙 — <b>추가만</b> 한다. 이미 MES 테이블이 있으면 아무것도 하지 않는다.
/// DROP 도, 컬럼 변경도 하지 않는다.
/// </summary>
public static class MesSchemaInitializer
{
    /// <summary>MES 테이블이 아직 없으면 EF 모델대로 만든다.</summary>
    public static async Task EnsureAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        // SQLite 파일처럼 DB 자체가 없을 수 있다 — 먼저 만들어 둔다.
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // 있는지 확인할 기준 테이블. Lot 은 MES 의 중심이라 이게 있으면 나머지도 있다.
        var marker = db.Model.FindEntityType(typeof(Lot))?.GetTableName()
            ?? throw new InvalidOperationException("Lot 테이블 매핑을 찾을 수 없습니다.");
        if (await TableExistsAsync(db, marker, cancellationToken)) return;

        // EF 가 이 DbContext 의 모델(=MES 테이블만)에 대한 생성 스크립트를 만든다.
        // 포털 테이블은 다른 DbContext 라 여기 포함되지 않는다.
        var script = db.Database.GenerateCreateScript();
        foreach (var batch in SplitBatches(script))
            await db.Database.ExecuteSqlRawAsync(batch, cancellationToken);

        Console.WriteLine($"[mes][schema] MES 테이블을 새로 만들었습니다 (기준: {marker}).");
    }

    private static async Task<bool> TableExistsAsync(
        ApplicationDbContext db, string table, CancellationToken cancellationToken)
    {
        try
        {
            // 행을 읽지 않고 존재만 본다. 없으면 공급자가 예외를 던진다.
            // 테이블 이름은 EF 모델에서 온 값이라 이어 붙여도 안전하다(사용자 입력이 아니다).
            // 매개변수로는 테이블 이름을 넣을 수 없어 이 자리에서만 경고를 끈다 — 빌드 경고가 하나 남아
            // 있으면 진짜 경고가 생겼을 때 묻힌다.
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM " + Quote(db, table) + " WHERE 1 = 0", cancellationToken);
#pragma warning restore EF1003
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(ApplicationDbContext db, string table)
        => db.Database.IsSqlServer() ? $"[{table}]" : $"\"{table}\"";

    /// <summary>GO 구분자가 섞여 있으면 나눠서 실행한다(SQL Server 스크립트).</summary>
    private static IEnumerable<string> SplitBatches(string script)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n');
        var buffer = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = string.Join("\n", buffer).Trim();
                if (batch.Length > 0) yield return batch;
                buffer.Clear();
                continue;
            }
            buffer.Add(line);
        }
        var last = string.Join("\n", buffer).Trim();
        if (last.Length > 0) yield return last;
    }
}
