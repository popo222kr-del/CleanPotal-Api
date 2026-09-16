using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// Lot-Product-Customer-ProcessDefinition Join이 필요한 조회 전용 경로 (ILotQueryRepository와 같은 이유로 분리).
public interface IProcessHistoryQueryRepository
{
    Task<IReadOnlyList<ProcessHistoryItemFullDto>> SearchAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default);

    // 2026-09-03 피드백: "세정 이력 조회"는 공정별 전체 이력이 아니라 LOT 단위로 현재 상태 한 줄만 보여준다.
    // 검색 조건(업체/세정코드/품목/OUT NO/제품명/S-N/공정/결과/입고일)에 맞는 LOT을 각각 1행으로 반환한다.
    Task<IReadOnlyList<LotListItemDto>> SearchLotCurrentAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default);

    // 2026-09-08 지시: "세정 이력 조회" 결과 컬럼 전면 개편 - 반출/반입 S/N, 분임조, 품목 구분, 무게,
    // 사용횟수, 합/부, 고객출고일까지 한 행에 담아 내려준다(LotHistoryRowDto 주석 참고).
    // 2026-09-09 2단계: 입고/출고 파라미터·검사 블록이 추가되며 열이 제품마다 달라져, 행과 함께
    // "이 결과에 필요한 동적 열 정의"(2단 그룹 헤더)를 같이 돌려준다.
    Task<LotHistoryResultDto> SearchLotHistoryAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default);
}
