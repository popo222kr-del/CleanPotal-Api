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
}
