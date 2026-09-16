namespace ProductionManagement.Domain.Entities;

// 일반 사용자가 수정/삭제할 수 없다 (CLAUDE.md 절대 금지사항). Repository/Service를 통한 Insert만 허용하고
// Update/Delete 경로 자체를 만들지 않는다. 조회 UI는 Phase 10(이력 조회)에서 추가한다.
public class AuditLog : Entity<int>
{
    public DateTime OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
