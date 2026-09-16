using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;

namespace ProductionManagement.Infrastructure.Sequencing;

// LOT 번호 형식(2026-08-21 피드백): "1{code}{yyMMdd}{일별순번:3자리}P" (예: 1SS260816003P).
// - 앞 "1"/끝 "P"는 고정 문자.
// - code는 전산등록 화면에서 사용자가 직접 입력하는 2자리 값 - 특정 코드 체계에서 오는 게 아니라
//   "임의의 값"(예: SS/SA/SU 등)이라 이 클래스는 그대로 대문자화해서 끼워 넣기만 한다.
// - yyMMdd는 LOT이 실제로 생성되는 시점(전산등록 실행일)의 날짜 - RegistrationCreateRequest의
//   고객출고일(ShipDate, 미래/과거일 수 있음)과는 다른 값이다.
// - 일별순번은 SystemSequences 기반 원자적 증가(SqliteExportNumberGenerator와 동일 패턴)이고, 코드별이
//   아니라 "그 날짜 전체"에서 공유되는 순번이다(사용자 확인: "그날(YYMMDD) 안에서만 초기화") - 그래서
//   같은 날 다른 코드로 등록해도 순번은 계속 이어진다.
// - LotNumber 자체의 유일성은 (날짜+그 날짜의 순번) 조합만으로 이미 보장되므로 code는 유일성에 관여하지
//   않는다 - 이 값은 순수하게 화면/현장에서 식별을 돕는 표시용 접두 코드다.
//
// MAX(LotNumber)+1 방식은 절대 사용하지 않는다 (CLAUDE.md 7번 / 절대 금지사항 4번) - 그래서
// SqliteExportNumberGenerator와 동일하게 읽지 않고 곧바로 원자적 UPDATE로 증가시킨다.
public class SqliteLotNumberGenerator : ILotNumberGenerator
{
    private readonly ApplicationDbContext _context;

    public SqliteLotNumberGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCode.Length != 2)
        {
            // RegistrationValidator.Validate가 이미 화면 입력 단계에서 걸러내므로 정상 흐름에서는
            // 도달하지 않는다 - 여기서는 프로그래밍 오류(다른 호출 경로의 실수)만 방어한다.
            throw new ArgumentException("LOT코드는 2자리여야 합니다.", nameof(code));
        }

        var dateKey = DateTime.Now.ToString("yyMMdd");
        var sequenceName = $"LOT-{dateKey}";

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SystemSequences SET CurrentValue = CurrentValue + 1 WHERE SequenceName = {sequenceName}",
            cancellationToken);

        if (rowsAffected == 0)
        {
            // 오늘 첫 LOT - 행이 아직 없으므로 1로 먼저 만든다. INSERT 자체가 유일 인덱스(SequenceName
            // Unique)로 보호되므로 두 트랜잭션이 동시에 첫 행을 만들려 하면 하나는 SQLite 제약 위반으로
            // 실패하고, 그 경우 이어서 증가시킨다(SqliteExportNumberGenerator와 동일 패턴).
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

        return $"1{normalizedCode}{dateKey}{newValue:D3}P";
    }
}
