using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// HOLD/RELEASE/재작업/SKIP/SHIP 정의 시트 5개 - 구조(CODE/DESC)가 동일하여 Category로만 구분되는
// 하나의 테이블로 통합한다 (5개의 거의 동일한 엔티티를 따로 두지 않음).
public class ReasonCode : Entity<int>
{
    public ReasonCategory Category { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
