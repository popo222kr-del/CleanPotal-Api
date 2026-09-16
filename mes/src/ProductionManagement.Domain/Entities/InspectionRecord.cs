namespace ProductionManagement.Domain.Entities;

// LOT 하나가 특정 OPER(주로 2100 입고검사/7000 출고검사)에서 기록한 파라미터 측정값 한 건.
// (LotId, ProcessDefinitionId, ParameterDefinitionId) 조합마다 값은 하나뿐 - 재입력하면 갱신한다
// (수기 측정값 UPDATE 자체는 "과거 이력 UPDATE 금지" 대상이 아니다 - 진행 중인 검사의 값 정정이며,
// 어느 OPER에서 기록됐는지(ProcessDefinitionId)로 IN INSP/FI INSP를 구분한다).
public class InspectionRecord : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public int ParameterDefinitionId { get; set; }
    public ParameterDefinition ParameterDefinition { get; set; } = null!;

    public string? InputValue { get; set; }
    public string? Comment { get; set; }

    public DateTime RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
}
