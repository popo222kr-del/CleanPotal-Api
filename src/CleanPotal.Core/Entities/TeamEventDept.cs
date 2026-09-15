namespace CleanPotal.Core.Entities;

/// <summary>
/// 일정 ↔ 부서 연결. 생산회의처럼 <b>여러 부서가 함께 들어가는 일정</b>이 있어
/// 일정에 부서 컬럼 하나를 두지 않고 별도 연결로 둔다.
///
/// 부서는 이름이 아니라 <see cref="OrgUnit.Id"/> 로 가리킨다 —
/// 부서 이름을 바꿔도 일정이 그대로 따라오게 하기 위해서다.
/// </summary>
public class TeamEventDept
{
    public int Id { get; set; }
    public int TeamEventId { get; set; }
    public int OrgUnitId { get; set; }
}
