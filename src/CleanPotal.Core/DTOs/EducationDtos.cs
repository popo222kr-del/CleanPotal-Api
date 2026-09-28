namespace CleanPotal.Core.DTOs;

public record EducationPlanDto(
    int Id, string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate,
    string Status, int Progress, string EduMethod, string AttachmentPath,
    int? DeptId = null);   // 대상자의 부서(조직도 Id) — 교육 현황은 부서마다 따로 본다

public record EducationUpsertRequest(
    string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate,
    string Status, int Progress, string EduMethod, string? AttachmentPath);
