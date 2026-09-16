using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// 2026-08-28: Windows 계정 기반에서 "아이디/비밀번호 로그인 계정"으로 전환. LoginId로 로그인하고,
// 세션 사용자는 로그인한 계정이 된다. WindowsAccount는 참고용(레거시)으로 남겨둔다(선택).
// 계정은 관리자가 "사용자 관리"에서 생성/권한지정/비밀번호 초기화한다(자동 프로비저닝 폐지).
public class User : Entity<int>
{
    // 로그인 아이디(유일). 세션 사용자·감사 로그 작성자 식별에 쓰인다.
    public string LoginId { get; set; } = string.Empty;

    // 표준 솔트 해시(PBKDF2). "iterations.base64(salt).base64(hash)" 형식으로 저장한다.
    public string PasswordHash { get; set; } = string.Empty;

    // 화면 표시용 이름(선택).
    public string? DisplayName { get; set; }

    // 2026-08-31 피드백(#5): 소속 팀명(분임조). 계정 정보에 표시/수정한다.
    public string? TeamName { get; set; }

    // 최초 로그인/초기화 후 비밀번호 변경을 유도한다.
    public bool MustChangePassword { get; set; }

    public bool IsActive { get; set; } = true;

    // 레거시(참고용). 더 이상 로그인 식별자가 아니다.
    public string? WindowsAccount { get; set; }

    public UserRole Role { get; set; } = UserRole.Worker;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
