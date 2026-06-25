namespace CleanPotal.Core.DTOs;

public record ZoneDto(string Key, string Label, string Group);

public record InspectionItemDto(int Id, string Zone, int SortOrder, string Text);

public record InspectionItemRequest(string Zone, string Text);

public record InspectionRecordDto(
    int Id, string Zone, DateOnly Date, string Shift, string Worker,
    int CheckedCount, int TotalCount, DateTime SubmittedAt);

public record SubmitInspectionRequest(
    string Zone, DateOnly Date, string Shift, int CheckedCount, int TotalCount);
