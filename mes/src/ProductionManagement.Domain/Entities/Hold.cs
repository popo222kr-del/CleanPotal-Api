namespace ProductionManagement.Domain.Entities;

// HOLD 발생/해제 이력. 삭제하지 않는다 (CLAUDE.md 절대 금지사항).
public class Hold : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public string RaisedBy { get; set; } = string.Empty;
    public DateTime RaisedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Remarks { get; set; }

    public bool IsReleased { get; set; }
    public string? ReleasedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }
    public string? ActionTaken { get; set; }
}
