using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// LOT 전반. 목록 검색부터 대시보드 집계(KPI/공정별 WIP/장기대기/TAT)까지 LOT을 읽는 일이
// 대부분 여기로 모인다. 공정 진행 자체는 여기가 아니라 ProcessTransitionService가 맡는다.
public interface ILotService
{
    Task<(IReadOnlyList<LotListItemDto> Items, int TotalCount)> SearchAsync(LotSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessWipDto>> GetProcessWipCountsAsync(CancellationToken cancellationToken = default);

    // 2026-08-31 피드백(#3): 대시보드 KPI/WIP 카드 클릭 시 제품 목록.
    Task<IReadOnlyList<OperLotItemDto>> GetDashboardLotsAsync(DashboardLotCategory category, int? operCode, CancellationToken cancellationToken = default);
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LotListItemDto>> GetLongWaitLotsAsync(int take, CancellationToken cancellationToken = default);
    Task<TatReportDto> GetTatReportAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);
    Task<LotDetailDto?> GetDetailAsync(int lotId, CancellationToken cancellationToken = default);
    Task<LotListItemDto> CreateAsync(LotCreateRequest request, CancellationToken cancellationToken = default);

    // 전산등록 시 자동 생성된 S/N이 실물 각인과 다를 때 수기로 고쳐 쓰기 위함.
    Task UpdateSerialNumberAsync(int lotId, string newSerialNumber, CancellationToken cancellationToken = default);

    // 2026-09-01 피드백(#2/#3): LOT의 현재 공정 최신 이력에 코멘트를 반영한다(LOT 정보 수정 창에서 편집).
    // OPER 코멘트/LOT 현황 조회의 해당 공정 코멘트와 같은 값(ProcessHistory.Comment)을 갱신한다.
    Task UpdateCurrentCommentAsync(int lotId, string? comment, CancellationToken cancellationToken = default);

    // 배치(Batch) - 같은 제품(=세정코드=업체, Product.CleaningCode가 NOT NULL+UNIQUE라 세정코드가 같으면
    // 업체도 자동으로 같다)의 LOT들을 하나로 묶는다. 대표 LOT은 사용자가 고르지 않고, 묶이는 LOT 중
    // LotNumber가 가장 빠른(=먼저 등록된) 것으로 서비스가 자동 결정한다(2026-08-20 사용자 확정).
    // 세정/건조 진행중·HOLD LOT은 대상에 포함할 수 없다. 최소 2개 이상이어야 한다.
    Task BatchAsync(IReadOnlyList<int> lotIds, CancellationToken cancellationToken = default);

    // lotIds 각각에 대해: 그 LOT이 배치의 대표면 그룹 전체를 해체하고, 멤버면 그 LOT만 그룹에서 뺀다.
    // Batch와 동일하게 세정/건조 진행중·HOLD LOT은 대상에서 제외된다.
    Task UnbatchAsync(IReadOnlyList<int> lotIds, CancellationToken cancellationToken = default);
}
