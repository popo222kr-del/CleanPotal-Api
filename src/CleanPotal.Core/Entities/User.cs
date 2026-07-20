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
    public string JobTitle { get; set; } = "";
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
}

/// <summary>영역 등급 상수.</summary>
public static class AccessLevel
{
    public const int None = 0;
    public const int View = 1;
    public const int Edit = 2;
}
