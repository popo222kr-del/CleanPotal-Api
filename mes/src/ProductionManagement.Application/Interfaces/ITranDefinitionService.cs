using ProductionManagement.Application.DTOs;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Interfaces;

// OPER 화면의 "TRAN 선택" 드롭다운을 채우는 조회 전용 서비스. 실제 실행은 IOperActionService.ExecuteTranAsync가 한다.
public interface ITranDefinitionService
{
    Task<IReadOnlyList<TranOptionDto>> GetAvailableTransitionsAsync(int lotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReasonCodeDto>> GetReasonCodesAsync(ReasonCategory category, CancellationToken cancellationToken = default);
}
