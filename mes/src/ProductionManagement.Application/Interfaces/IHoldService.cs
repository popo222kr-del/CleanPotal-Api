using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// HOLD(작업 보류) 조회와 해제. "공정관리 > HOLD 관리" 화면이 쓴다.
// HOLD를 거는 것은 여기가 아니라 OPER 화면의 TRAN 실행(ProcessTransitionService)이다.
public interface IHoldService
{
    Task<IReadOnlyList<HoldDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ReleaseAsync(HoldReleaseRequest request, CancellationToken cancellationToken = default);
}
