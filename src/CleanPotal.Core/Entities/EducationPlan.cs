namespace CleanPotal.Core.Entities;

/// <summary>교육 계획 (WPF dispatch.db EducationPlan). 교육 현황 대시보드.</summary>
public class EducationPlan
{
    public int Id { get; set; }
    public string MemberName { get; set; } = "";
    public string CourseName { get; set; } = "";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = "대기";   // 대기/신청완료/취소/진행/완료
    public int Progress { get; set; }
    public string EduMethod { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    /// <summary>교육 대상자의 부서(조직도 Id). 교육 현황은 부서마다 따로 본다(DeptScope).</summary>
    public int? DeptId { get; set; }
}
