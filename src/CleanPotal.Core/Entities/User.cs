namespace CleanPotal.Core.Entities;

/// <summary>
/// 사용자 계정 (기존 WPF UserModel + users.json 통합).
/// 비밀번호는 평문 저장하지 않고 해시로 보관한다.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public string RealName { get; set; } = "";
    public string Department { get; set; } = "";   // 부서 (예: 세정팀 / 품질팀 / Office)
    public string TeamName { get; set; } = "";      // 소속팀 (예: 김팀 / 장팀)

    /// <summary>직급 — 사원·주임·대리·과장·차장·부장·상무·전무·부사장·사장. <see cref="CleanPotal.Core.JobRank"/>.
    /// 직위(JobTitle)와 별개다: 직급은 호칭, 직위는 맡은 일(QA팀장·세정팀장 등).</summary>
    public string Rank { get; set; } = "";

    public string JobTitle { get; set; } = "";      // 직위 (맡은 일: QA팀장 / 세정팀장 …)
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string EmployeeNumber { get; set; } = "";
    public string HireDate { get; set; } = "";

    public bool IsResigned { get; set; }
    public string ResignDate { get; set; } = "";

    // ── 권한: 영역 × 등급 (0=없음/메뉴 숨김, 1=조회 전용, 2=편집) ──
    public bool IsAdmin { get; set; }                 // 관리자: 전체 권한 + 관리자 영역
    public int AccessSchedule { get; set; } = 1;      // 일정관리 (세정팀 달력·자재물류 일정)
    public int AccessRoster { get; set; } = 1;        // 근무표 (도장/교대 입력)
    public int AccessHandover { get; set; } = 1;      // 현장 인수인계 (인수인계·주간세정·미팅·요청·스케줄보드·배차·공지·업체)
    public int AccessField { get; set; } = 1;         // 현장 점검 (재고·ICP-MS·체크시트)
    public int AccessOffice { get; set; }             // OFFICE 업무 (견적·주간보고·BROKEN·교육·분장표·포탈) — 기본 없음
    // MES (생산관리) — LOT 현황·공정·전산등록. 현장 점검과 별개로 준다.
    // 현장 점검 권한에 얹어 두면 "MES 만 쓰는 사람"·"MES 는 빼는 사람"을 만들 수 없다.
    public int AccessMes { get; set; } = 1;           // 기본 조회 — 전 직원이 쓰는 시스템이라 잠가 두지 않는다

    // 사용자별로 숨길 하위 메뉴 경로 (JSON 배열 문자열, 예: ["/meeting","/broken"]).
    // 상위 영역 등급은 조회/편집을 결정하고, 이 목록에 든 개별 메뉴만 추가로 숨긴다.
    public string HiddenMenus { get; set; } = "";
}

/// <summary>영역 등급 상수.</summary>
public static class AccessLevel
{
    public const int None = 0;
    public const int View = 1;
    public const int Edit = 2;
}
