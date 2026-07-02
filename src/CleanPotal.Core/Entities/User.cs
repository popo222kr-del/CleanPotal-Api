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
    public string TeamName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string EmployeeNumber { get; set; } = "";
    public string HireDate { get; set; } = "";

    public bool IsResigned { get; set; }
    public string ResignDate { get; set; } = "";

    // 권한 플래그 (WPF SessionManager 권한 8종 + 최고관리자)
    public bool IsAdmin { get; set; }
    public bool CanManageFiles { get; set; }
    public bool CanManageNotices { get; set; }
    public bool CanManageVendors { get; set; }
    public bool CanManageSchedule { get; set; }
    public bool CanManageBroken { get; set; }
    public bool CanAccessEtcMenu { get; set; }
    public bool CanManageShiftBoard { get; set; }
    public bool CanManageInventory { get; set; }
}
