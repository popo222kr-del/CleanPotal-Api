namespace CleanPotal.Core.Entities;

/// <summary>개인별 업무 분장표 — 인원 (WPF WorkAssignmentMembers).</summary>
public class WorkMember
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public bool IsHidden { get; set; }
    public string ResignDate { get; set; } = "";
}

/// <summary>개인별 업무 분장표 — 계정 (WPF WorkAssignmentAccounts).</summary>
public class WorkAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string AccountPassword { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>개인별 업무 분장표 — 교육 이수 (WPF WorkAssignmentEduBasic).</summary>
public class WorkEdu
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string EduName { get; set; } = "";
    public string EduDate { get; set; } = "";
    public string Instructor { get; set; } = "";
    public string Note { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
}
