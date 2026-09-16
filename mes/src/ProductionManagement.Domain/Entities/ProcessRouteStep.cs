namespace ProductionManagement.Domain.Entities;

// StepOrder 순서가 곧 Process State Machine이 허용하는 전이 순서다 (공정 순서 하드코딩 금지, CLAUDE.md 8번).
public class ProcessRouteStep : Entity<int>
{
    public int ProcessRouteId { get; set; }
    public ProcessRoute ProcessRoute { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public int StepOrder { get; set; }
}
