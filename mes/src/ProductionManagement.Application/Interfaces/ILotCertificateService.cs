namespace ProductionManagement.Application.Interfaces;

// 2026-08-27 PM↔성적서 병합 Phase 3: 전산등록 시 제품에 등록된 성적서 양식을 기준으로 LOT별 성적서
// (양식 복사본, 제목=LOT번호)를 INSPECTION 폴더에 생성하고 문서로 등록한다. 양식이 없으면 아무것도
// 하지 않는다. 등록 흐름을 깨지 않도록 실패해도 예외를 던지지 않고 false를 반환한다.
public interface ILotCertificateService
{
    Task<bool> TryGenerateForLotAsync(int lotId, CancellationToken cancellationToken = default);
}
