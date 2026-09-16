using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;

namespace ProductionManagement.Infrastructure.Sequencing;

// SqliteLotNumberGenerator와 동일한 원자적 증가 패턴(읽고-쓰는 방식은 Lost Update 위험 - CLAUDE.md 7번)을
// 재사용하되, 키를 업체×날짜별로 동적으로 만든다. LotNumber와 달리 이 시퀀스는 매일 0부터 초기화되는
// 것이 의도된 동작이다 (반출번호는 LotNumber와 다른 개념 - CLAUDE.md 절대 금지사항 4번은 LotNumber 자체에만 적용).
public class SqliteExportNumberGenerator : IExportNumberGenerator
{
    private readonly ApplicationDbContext _context;

    public SqliteExportNumberGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextAsync(int customerId, string exportPrefix, DateTime shipDate, CancellationToken cancellationToken = default)
    {
        var dateKey = shipDate.ToString("yyyyMMdd");
        var sequenceName = $"EXPORT-{customerId}-{dateKey}";

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SystemSequences SET CurrentValue = CurrentValue + 1 WHERE SequenceName = {sequenceName}",
            cancellationToken);

        if (rowsAffected == 0)
        {
            // 이 업체·이 날짜의 첫 반출번호 - 행이 아직 없으므로 0으로 먼저 만든 뒤 같은 트랜잭션에서 증가시킨다.
            // INSERT 자체가 유일 인덱스(SequenceName Unique)로 보호되므로 두 트랜잭션이 동시에 첫 행을
            // 만들려 하면 하나는 SQLite 제약 위반으로 실패하고, 그 경우 이어서 증가시킨다.
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO SystemSequences (SequenceName, CurrentValue) VALUES ({sequenceName}, 1)",
                    cancellationToken);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE SystemSequences SET CurrentValue = CurrentValue + 1 WHERE SequenceName = {sequenceName}",
                    cancellationToken);
            }
        }

        var newValue = await _context.SystemSequences
            .AsNoTracking()
            .Where(s => s.SequenceName == sequenceName)
            .Select(s => s.CurrentValue)
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return $"{exportPrefix}{dateKey.Substring(2)}-{newValue}";
    }
}
