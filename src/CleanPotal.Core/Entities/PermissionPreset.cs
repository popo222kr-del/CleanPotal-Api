namespace CleanPotal.Core.Entities;

/// <summary>
/// 권한 프리셋(역할) — 영역 등급 + 메뉴별 '조회만'·숨김 묶음. 사용자 계정 관리에서 사람에게 한 번에 적용한다.
/// 관리자가 화면에서 만들고 고친다. 처음 열 때 비어 있으면 기본 프리셋으로 채운다.
/// 적용은 그 순간의 값을 사람에게 복사하는 것이다 — 프리셋을 나중에 고쳐도 이미 적용한 사람은 바뀌지 않는다.
/// </summary>
public class PermissionPreset
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public int AccessSchedule { get; set; }
    public int AccessRoster { get; set; }
    public int AccessHandover { get; set; }
    public int AccessField { get; set; }
    public int AccessMaterial { get; set; }
    public int AccessOffice { get; set; }
    public int AccessMes { get; set; }
    /// <summary>조회만 메뉴 경로 JSON 배열</summary>
    public string ReadOnlyMenus { get; set; } = "[]";
    /// <summary>숨길 메뉴 경로 JSON 배열</summary>
    public string HiddenMenus { get; set; } = "[]";
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
