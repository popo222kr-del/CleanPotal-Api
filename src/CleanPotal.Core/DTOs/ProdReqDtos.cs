namespace CleanPotal.Core.DTOs;

public record ProdReqDto(
    int Id,
    DateOnly? RequestDate,
    DateOnly? DueDate,
    string Status,
    string Category,
    string Location,
    string RequestDetail,
    string Requester,
    DateOnly? ActionDate,
    string ActionDetail,
    string Assignee,
    DateTime CreatedAt
);

public record ProdReqUpsertRequest(
    DateOnly? RequestDate,
    DateOnly? DueDate,
    string Category,
    string Location,
    string RequestDetail,
    DateOnly? ActionDate,
    string ActionDetail,
    string Assignee
);

public record ProdReqStatusRequest(string Status);
