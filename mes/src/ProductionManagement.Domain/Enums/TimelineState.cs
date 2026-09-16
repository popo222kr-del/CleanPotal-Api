namespace ProductionManagement.Domain.Enums;

// Lot 상세 화면의 Process Timeline에서 각 공정 단계가 어떤 상태인지 (CLAUDE.md: 색상만으로 표현하지 않음 -
// 아이콘+텍스트를 함께 쓰기 위해 Wpf에서 이 값을 기준으로 아이콘을 고른다).
public enum TimelineState
{
    Pending = 0,
    Active = 1,
    Completed = 2,
    Hold = 3,
    Rework = 4
}
