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

    /// <summary>
    /// 생산팀인가(팀 행에만 의미가 있다). 근무표에 나올지, 통계에서 생산직으로 셀지를 가른다.
    ///
    /// 교대조와는 <b>별개 축</b>이다 — 주간팀처럼 교대는 없지만 생산인 팀이 있다.
    /// 반대로 <see cref="ShiftGroup"/> 이 지정된 팀은 정의상 생산팀이므로,
    /// 읽을 때 <c>IsProduction || ShiftGroup &gt; 0</c> 으로 본다. 그래서 이 칸이 추가되기
    /// 전부터 있던 교대 팀들도 따로 손보지 않아도 근무표에서 사라지지 않는다.
    /// </summary>
    public bool IsProduction { get; set; }

    /// <summary>
    /// 이 팀이 WPF 에서 쓰던 이름들(쉼표 구분). 예: 팀 이름을 "김팀" → "1팀" 으로 바꿨다면 "김팀".
    ///
    /// WPF 와 웹을 함께 쓰는 동안 WPF 는 옛 이름을 계속 기록한다. 임포트할 때 이 목록을 보고
    /// 현재 이름으로 바꿔 넣어야, WPF 에서 새로 들어온 직원이나 근무 기록이 옛 팀에 묶여
    /// 근무표에서 사라지는 일이 없다. WPF 를 끄면 비워도 된다.
    /// </summary>
    public string LegacyNames { get; set; } = "";

    /// <summary>
    /// 달력에서 쓸 색 (#RRGGBB). 비워 두면 Id 기준으로 자동 배정한다.
    /// 순서가 아니라 Id 기준이라, 다른 부서를 지워도 남은 부서의 색이 밀리지 않는다.
    /// </summary>
    public string Color { get; set; } = "";

    /// <summary>
    /// 달력 칸에 붙일 약칭(2~4자). 비워 두면 이름 앞부분을 쓴다.
    /// 색만으로 구분하면 색약이신 분과 흑백 인쇄에서 구분이 안 되므로 글자를 함께 붙인다.
    /// </summary>
    public string ShortName { get; set; } = "";

    /// <summary>
    /// 사용 중인 조직인가. 폐지된 부서는 <b>삭제하지 않고</b> 이 값을 false 로 둔다.
    /// 지워 버리면 그 부서에 달려 있던 과거 일정이 고아가 된다.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
