namespace CleanPotal.Core.Entities;

/// <summary>
/// 근무 일정 (기존 WPF ShiftScheduleModel / shift_schedule 테이블).
/// 근무표 도장 기능이 이 테이블을 Upsert 한다.
/// ShiftType 예: 주간, 야간, 휴무, 연차, 오전반차, 오후반차, 반반차(시간대), 특근, 교육, 비우기
/// </summary>
public class ShiftSchedule
{
    public int Id { get; set; }

    public string MemberName { get; set; } = "";   // 사용자 실명
    public DateOnly TargetDate { get; set; }
    public string ShiftType { get; set; } = "";
    public string TeamGroup { get; set; } = "";     // 김팀 / 장팀 등

    public string CreatorName { get; set; } = "";
    public DateTime CreateDate { get; set; } = DateTime.Now;
}
