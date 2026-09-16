using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// OPER(공정) 화면 상단 목록 조회. 그 공정에 지금 있는 LOT만 뽑아 온다.
// 배치(여러 LOT을 묶어 한 번에 처리)의 구성원 목록도 여기서 가져온다.
public interface IOperQueryService
{
    Task<IReadOnlyList<OperLotItemDto>> SearchAsync(OperLotSearchRequest request, CancellationToken cancellationToken = default);

    // 2026-08-28 피드백(#4): 배치 대표 LOT의 묶인 LOT 목록(대표 포함).
    Task<IReadOnlyList<BatchMemberDto>> GetBatchMembersAsync(int representativeLotId, CancellationToken cancellationToken = default);
}
