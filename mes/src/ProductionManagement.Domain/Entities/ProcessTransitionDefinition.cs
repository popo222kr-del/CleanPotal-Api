using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// "기타프로그램 마스터 데이터.xlsx"의 TRAN 사용 공정 시트를 그대로 옮긴 Master Data (2026-08-18 사용자 재확인본,
// 46행). OPER 화면의 작업 흐름은 더 이상 ProcessRouteStep.StepOrder 하나로 정해지지 않고, 작업자가 현재 OPER에서
// 유효한 TRAN 중 하나를 직접 선택해 실행하는 방식으로 바뀐다 (같은 OPER+같은 TRAN CODE라도 제품 경로에 따라
// TargetOperCode가 다른 행이 여러 개 존재할 수 있음 - 예: 4000 건조의 END는 7000/5000 두 갈래).
//
// TargetOperCode가 null인 경우는 TRAN CODE=Ship(T530, 8100 고객출하)뿐이다: 더 넘어갈 다음 OPER이 없는
// 종결 처리이며, Lot은 현재 OPER(8100)에 남은 채 CurrentStatus만 Completed로 바뀐다.
public class ProcessTransitionDefinition : Entity<int>
{
    public string TranId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int SourceOperCode { get; set; }
    public TranCode TranCode { get; set; }
    public int? TargetOperCode { get; set; }

    public bool IsActive { get; set; } = true;
}
