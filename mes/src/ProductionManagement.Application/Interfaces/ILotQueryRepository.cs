using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// Lot ↔ Product ↔ Customer Join과 Skip/Take Paging이 필요한 조회 전용 경로.
// 제네릭 IRepository로는 이런 조회를 DB 단에서 효율적으로 표현하기 어려워 별도로 둔다 (CLAUDE.md 성능 원칙:
// 전체 Memory Filtering 금지, DB Query 단계에서 Filtering/Paging).
public interface ILotQueryRepository
{
    Task<(IReadOnlyList<LotListItemDto> Items, int TotalCount)> SearchAsync(LotSearchRequest request, CancellationToken cancellationToken = default);
    Task<LotDetailHeaderDto?> GetHeaderAsync(int lotId, CancellationToken cancellationToken = default);

    // OPER 화면 전용: 특정 공정에 "현재" 있는 Lot만, 작업자/공정 시작시간까지 포함해서 가져온다.
    Task<IReadOnlyList<OperLotItemDto>> SearchOperLotsAsync(OperLotSearchRequest request, CancellationToken cancellationToken = default);

    // 2026-08-31 피드백(#3): 대시보드 카드(카테고리/공정) 클릭 시 해당 제품 목록.
    Task<IReadOnlyList<OperLotItemDto>> GetDashboardLotsAsync(DashboardLotCategory category, int? operCode, CancellationToken cancellationToken = default);

    // 2026-08-28 피드백(#4): 배치 대표 LOT의 묶인 LOT 목록(대표 포함) - LOT번호/S-N만.
    Task<IReadOnlyList<BatchMemberDto>> GetBatchMembersAsync(int representativeLotId, CancellationToken cancellationToken = default);

    // 전체 공정 조회: 공정별 현재 WIP 수. ProcessRouteStep 순서대로 정렬해서 반환한다.
    Task<IReadOnlyList<ProcessWipDto>> GetProcessWipCountsAsync(CancellationToken cancellationToken = default);

    // Dashboard KPI 6종 + 최근 30일 완료 기준 평균 TAT.
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);

    // Dashboard 하단 "장기대기 LOT" 패널 - GetDashboardSummaryAsync의 LongWait 카운트와 같은 기준
    // (SystemSettings LongWait.{공정코드}.DelayHours)으로 실제 LOT 목록을 내려준다. 가장 오래 지연된
    // 순서로 정렬한다.
    Task<IReadOnlyList<LotListItemDto>> GetLongWaitLotsAsync(int take, CancellationToken cancellationToken = default);

    // 선택 기간(dateFrom~dateTo, 포함)에 고객출하 완료된 LOT들의 TAT 요약 + 목록.
    Task<TatReportDto> GetTatReportAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);
}
