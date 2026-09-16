namespace ProductionManagement.Domain.Enums;

// HOLD/RELEASE/재작업/SKIP/SHIP 정의 시트 5개를 하나의 ReasonCode 엔티티로 통합할 때 쓰는 구분자.
public enum ReasonCategory
{
    Hold = 0,
    Release = 1,
    Rework = 2,
    Skip = 3,
    Ship = 4
}
