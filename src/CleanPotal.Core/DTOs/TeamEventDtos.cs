namespace CleanPotal.Core.DTOs;

public record TeamEventDto(
    int Id,
    string RegisteredBy,
    DateOnly StartDate,
    DateOnly EndDate,
    string Content,
    string Detail,
    DateTime CreateDate
);

public record TeamEventRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string Content,
    string Detail
);
