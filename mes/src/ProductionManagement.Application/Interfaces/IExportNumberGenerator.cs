namespace ProductionManagement.Application.Interfaces;

// 반출번호 = {ExportPrefix}{ShipDate:yyMMdd}-{그 업체·그 날짜의 순번}. LotNumber(전역 단조 증가,
// 업체/날짜별 초기화 절대 금지 - CLAUDE.md 절대 금지사항 4번)와는 별개 개념으로, 이 시퀀스는
// 의도적으로 업체×날짜별로 매일 0부터 초기화된다.
public interface IExportNumberGenerator
{
    Task<string> NextAsync(int customerId, string exportPrefix, DateTime shipDate, CancellationToken cancellationToken = default);
}
