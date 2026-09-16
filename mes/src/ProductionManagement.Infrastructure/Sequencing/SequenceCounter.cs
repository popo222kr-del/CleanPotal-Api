using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Infrastructure.Data;

namespace ProductionManagement.Infrastructure.Sequencing;

/// <summary>
/// SystemSequences 를 이용한 원자적 순번 증가.
///
/// 읽고-쓰는 방식(SELECT 후 +1)이나 MAX(..)+1 은 쓰지 않는다 — 동시에 두 요청이 들어오면
/// 같은 번호가 두 번 나온다(Lost Update). 대신 곧바로 UPDATE 로 증가시키고, 그 행이 아직
/// 없으면 INSERT 한다. INSERT 는 SequenceName 유일 인덱스가 지켜 주므로, 두 트랜잭션이 동시에
/// 첫 행을 만들려 하면 하나만 성공하고 나머지는 이어서 증가시킨다.
///
/// 예전에는 SQLite 전용이었다(SqliteException 을 직접 잡고 테이블 이름을 박아 썼다).
/// 포털과 같은 SQL Server 를 쓰게 되면서 공급자에 기대지 않도록 바꿨다.
/// </summary>
internal static class SequenceCounter
{
    /// <summary>이 이름의 순번을 1 올리고 올라간 값을 돌려준다.</summary>
    public static async Task<long> NextAsync(
        ApplicationDbContext context, string sequenceName, CancellationToken cancellationToken)
    {
        // 테이블 이름은 EF 모델에서 얻는다 — 접두사(Mes…)가 붙어도 따라온다.
        // 사용자 입력이 아니라 모델 메타데이터라 문자열로 이어 붙여도 안전하다.
        var table = context.Model.FindEntityType(typeof(SystemSequence))?.GetTableName()
            ?? throw new InvalidOperationException("SystemSequence 테이블 매핑을 찾을 수 없습니다.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // 테이블 이름만 이어 붙이고 값은 {0} 파라미터로 넘긴다.
        // ExecuteSqlRaw 에 보간 문자열을 바로 주면 EF1002(주입 위험) 경고가 난다.
        var bump = "UPDATE " + table + " SET CurrentValue = CurrentValue + 1 WHERE SequenceName = {0}";
        var insert = "INSERT INTO " + table + " (SequenceName, CurrentValue) VALUES ({0}, 1)";

        var rowsAffected = await context.Database.ExecuteSqlRawAsync(
            bump, new object[] { sequenceName }, cancellationToken);

        if (rowsAffected == 0)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    insert, new object[] { sequenceName }, cancellationToken);
            }
            catch (DbException)
            {
                // 같은 순간 다른 요청이 먼저 만들었다 — 유일 인덱스가 막아 준 것이므로 이어서 올린다.
                await context.Database.ExecuteSqlRawAsync(
                    bump, new object[] { sequenceName }, cancellationToken);
            }
        }

        var newValue = await context.SystemSequences
            .AsNoTracking()
            .Where(s => s.SequenceName == sequenceName)
            .Select(s => s.CurrentValue)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return newValue;
    }
}
