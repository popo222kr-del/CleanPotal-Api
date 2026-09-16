using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Application.Services;

// OPER 화면 목록 조회 구현. "지금 이 공정에 있는 LOT"만 뽑는다 - 화면마다 OPER 번호가 다르므로
// 같은 코드가 9개 공정 화면을 전부 감당한다.
// 배치 대표 LOT을 고르면 묶인 LOT 목록도 여기서 함께 가져온다.
public class OperQueryService : IOperQueryService
{
    private readonly ILotQueryRepository _lotQueryRepository;

    public OperQueryService(ILotQueryRepository lotQueryRepository)
    {
        _lotQueryRepository = lotQueryRepository;
    }

    public Task<IReadOnlyList<OperLotItemDto>> SearchAsync(OperLotSearchRequest request, CancellationToken cancellationToken = default)
        => _lotQueryRepository.SearchOperLotsAsync(request, cancellationToken);

    public Task<IReadOnlyList<BatchMemberDto>> GetBatchMembersAsync(int representativeLotId, CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetBatchMembersAsync(representativeLotId, cancellationToken);
}
