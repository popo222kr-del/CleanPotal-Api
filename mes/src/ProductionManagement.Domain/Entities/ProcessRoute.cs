namespace ProductionManagement.Domain.Entities;

// 공정 플로우(라우팅) - 제품이 거쳐 갈 공정 순서를 하나로 묶은 것. 예: 입고 -> 입고검사 -> 세정 ->
// 건조 -> 출고검사 -> 포장 -> 출하. 실제 순서는 Steps(ProcessRouteStep)의 StepOrder가 정한다.
// 제품마다 이 플로우를 배정하고(ProductProcessFlow), OPER 화면의 다음 공정 판단이 여기서 나온다.
// "셋업 > 공정 관리" 화면에서 만든다.
public class ProcessRoute : Entity<int>
{
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public List<ProcessRouteStep> Steps { get; set; } = new();
}
