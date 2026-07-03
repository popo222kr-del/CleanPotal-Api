namespace CleanPotal.Core.DTOs;

public record EducationPlanDto(
    int Id, string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate,
    string Status, int Progress, string EduMethod, string AttachmentPath);

public record EducationUpsertRequest(
    string MemberName, string CourseName, DateOnly? StartDate, DateOnly? EndDate,
    string Status, int Progress, string EduMethod, string? AttachmentPath);
