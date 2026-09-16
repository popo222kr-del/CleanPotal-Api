namespace ProductionManagement.Domain.Entities;

// 이름을 "Process"가 아니라 "ProcessDefinition"으로 둔다: System.Diagnostics.Process와의 네임스페이스 충돌을 피하고,
// "공정 실행 이력(ProcessHistory)"과 "공정 마스터 정의"를 이름에서부터 구분하기 위함.
public class ProcessDefinition : Entity<int>
{
    public string ProcessCode { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;

    // Phase 14 OPER 코드 체계 (1000=전산등록, 2000=입고, 2100=입고검사, 3000=세정, 4000=건조,
    // 4100=Laser&CO2, 5000=Bake, 7000=출고검사, 7100=포장완료, 8100=고객출하). 화면 표시/정렬용이며
    // 실제 진행 순서는 여전히 ProcessRouteStep.StepOrder가 결정한다 (이 값은 하드코딩된 순서로 쓰지 않는다).
    public int OperCode { get; set; }

    public bool IsActive { get; set; } = true;
}
