namespace ProductionManagement.Domain.Entities;

// "품목 공정 플로우 설정" 화면(2026-08-18 실사용 화면 참고)의 "부여된 플로우" 목록. ProcessRoute를
// 그대로 "플로우"로 재사용한다(F0001~ 등 이름 붙은 공정 순서 자체가 이미 ProcessRoute+ProcessRouteStep
// 구조와 동일함 - 별도 엔티티를 새로 만들지 않음). 이 목록은 참고/정책용 메타데이터이며, 실제 OPER 화면의
// TRAN 실행은 이 목록에 없는 TRAN도 막지 않는다(전이 허용 여부는 ProcessTransitionDefinition이 결정).
public class ProductProcessFlow : Entity<int>
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProcessRouteId { get; set; }
    public ProcessRoute ProcessRoute { get; set; } = null!;

    // "부여된 플로우" 목록 내 표시 순서.
    public int SortOrder { get; set; }
}
