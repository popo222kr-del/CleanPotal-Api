namespace CleanPotal.Core.Entities;

/// <summary>
/// 관리자가 화면에서 고친 공휴일. 코드에 적힌 기본 목록(HolidayService) 위에 덮어쓴다.
/// 임시공휴일·선거일처럼 갑자기 정해지는 날이나, 기본 목록이 아직 없는 해의 공휴일을 배포 없이 넣기 위해 둔다.
/// 날짜마다 한 행만 있다.
/// </summary>
public class HolidayOverride
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>쉬는 날로 둘 때 보일 이름.</summary>
    public string Name { get; set; } = "";
    /// <summary>true = 공휴일로 추가(또는 이름 변경), false = 기본 목록의 공휴일을 평일로 되돌림.</summary>
    public bool IsOff { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
