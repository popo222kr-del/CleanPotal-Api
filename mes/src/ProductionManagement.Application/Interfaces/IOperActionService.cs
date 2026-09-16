using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// OPER 화면의 "TRAN 선택 -> 실행" 하나의 진입점. 예전 4버튼(작업시작/공정완료/HOLD/재작업) 각각을 부르던
// 방식은 TRAN CODE 전이 엔진 도입(2026-08-18)으로 폐지되었다 - 어떤 동작을 할지는 더 이상 코드가 아니라
// ProcessTransitionDefinition Master Data(TranId)가 결정한다.
public interface IOperActionService
{
    Task ExecuteTranAsync(OperExecuteTranRequest request, CancellationToken cancellationToken = default);
}
