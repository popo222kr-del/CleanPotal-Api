namespace ProductionManagement.Domain.Entities;

// "재작업하기로 결정했다"는 관리 이벤트. 실제 재작업 시도 자체(작업자/시간/결과)는 ProcessHistory의
// AttemptNumber로 이미 추적된다 - 이 엔티티는 왜/누가 재작업을 결정했는지를 남긴다.
public class Rework : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public int AttemptNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DecidedBy { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; }
    public string? Remarks { get; set; }
}
