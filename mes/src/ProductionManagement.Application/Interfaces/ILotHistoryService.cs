using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// "LOT 현황 조회" 화면(우클릭 팝업 + 상단 메뉴 화면 둘 다 공유) 전용 조회 서비스.
public interface ILotHistoryService
{
    Task<LotHistoryHeaderDto> GetHeaderAsync(int lotId, CancellationToken cancellationToken = default);

    // 같은 S/N을 가진 모든 LOT(=같은 물리 부품이 여러 번 입고/전산등록된 이력 전체)의 TRAN 이력을
    // 시간순으로 합쳐서 보여준다(2026-08-19 피드백: "S/N 기준으로 누적으로 입고~출고 되었던 이전
    // 이력을 확인할 수 있게"). 단일 Lot의 이력만 보고 싶어도 그 Lot의 S/N으로 호출하면 된다.
    Task<IReadOnlyList<LotTransitionRowDto>> GetTransitionsBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default);

    // "LOT 현황 조회" 화면 좌측 하단 - 같은 S/N을 가진 입고/전산등록 사이클(=Lot)별 입고일자/출고일자
    // (2026-08-19 피드백).
    Task<IReadOnlyList<LotCycleRowDto>> GetCyclesBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default);

    // "LOT 현황 조회" 화면 우측 하단 - 같은 S/N을 가진 모든 LOT의 검사 파라미터 측정값(2026-08-19 피드백).
    Task<IReadOnlyList<LotParameterRecordDto>> GetParameterRecordsBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default);

    // 상단 메뉴 "LOT 현황 조회" 화면의 수기 검색(LOT/S-N/OUT NO) - 입력된 조건을 모두 만족하는 LOT 중
    // 가장 최근 갱신된 것 하나를 찾는다. 아무 조건도 없으면 null.
    Task<int?> FindLotIdAsync(string? lotNumber, string? serialNumber, string? exportNumber, CancellationToken cancellationToken = default);

    // 통합 검색: 입력값 하나를 LOT / S/N / 반출번호에 각각 대조해 OR로 찾는다(2026-09-08).
    Task<int?> FindLotIdByKeywordAsync(string? keyword, CancellationToken cancellationToken = default);
}
