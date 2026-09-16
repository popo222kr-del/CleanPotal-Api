using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Interfaces;

// Phase 8(OPER 화면)에서 "작업 시작/공정 완료/HOLD/재작업" 버튼을 누를 때마다 실제 상태 변경 전에 호출한다.
// 허용되지 않은 전이면 InvalidProcessTransitionException을 던진다 - UI에서 막았어도 여기서 다시 검증한다.
public interface IProcessTransitionService
{
    Task EnsureTransitionAllowedAsync(int lotId, int targetProcessDefinitionId, LotStatus targetStatus, CancellationToken cancellationToken = default);
}
