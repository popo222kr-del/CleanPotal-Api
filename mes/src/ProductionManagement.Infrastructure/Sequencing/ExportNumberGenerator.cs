using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;

namespace ProductionManagement.Infrastructure.Sequencing;

// LotNumberGenerator와 동일한 원자적 증가 패턴(읽고-쓰는 방식은 Lost Update 위험 - CLAUDE.md 7번)을
// 재사용하되, 키를 업체×날짜별로 동적으로 만든다. LotNumber와 달리 이 시퀀스는 매일 0부터 초기화되는
// 것이 의도된 동작이다 (반출번호는 LotNumber와 다른 개념 - CLAUDE.md 절대 금지사항 4번은 LotNumber 자체에만 적용).
public class ExportNumberGenerator : IExportNumberGenerator
{
    private readonly ApplicationDbContext _context;

    public ExportNumberGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextAsync(int customerId, string exportPrefix, DateTime shipDate, CancellationToken cancellationToken = default)
    {
        var dateKey = shipDate.ToString("yyyyMMdd");
        var newValue = await SequenceCounter.NextAsync(
            _context, $"EXPORT-{customerId}-{dateKey}", cancellationToken);

        return $"{exportPrefix}{dateKey.Substring(2)}-{newValue}";
    }
}
