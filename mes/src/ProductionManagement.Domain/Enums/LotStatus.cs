namespace ProductionManagement.Domain.Enums;

// Lot.CurrentStatus: "현재 공정에서" Lot이 어떤 상태인지. 어느 공정인지는 Lot.CurrentProcessDefinitionId가 별도로 갖는다
// (CLAUDE.md 8번: CurrentProcess와 CurrentStatus 분리 원칙).
public enum LotStatus
{
    Waiting = 0,
    InProgress = 1,
    Hold = 2,
    Rework = 3,
    Completed = 4,
    Cancelled = 5,
    Void = 6
}
