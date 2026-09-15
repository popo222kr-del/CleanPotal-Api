namespace CleanPotal.Core.Entities;

/// <summary>조직 단위(부서/팀) 등록부. 소속 인원이 없어도 부서·팀을 미리 만들어 둘 수 있다.
/// (연구소 등 신규 부서 대비) Kind = "dept" | "team". team은 Parent에 부서명을 둔다.</summary>
public class OrgUnit
{
    public int Id { get; set; }
    public string Kind { get; set; } = "dept";   // dept | team
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";     // team이면 소속 부서명
    public int OrderIndex { get; set; }

    /// <summary>
    /// 교대 근무 조. 0 = 교대 없음(주간팀·Office 등), 1 = 1조, 2 = 2조.
    ///
    /// 1조와 2조는 항상 반대 근무(한쪽이 주간이면 다른 쪽은 야간)다.
    /// 근무 예측·근무표·달력은 <b>팀 이름이 아니라 이 값</b>을 보므로,
    /// 팀 이름을 바꿔도 일정이 그대로 따라온다.
    /// </summary>
    public int ShiftGroup { get; set; }
}
