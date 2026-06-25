namespace CleanPotal.Core.DTOs;

public record HandoverDto(
    int Id,
    string Vendor,
    string Category,
    string Owner,
    string Content,
    DateOnly? InDate,
    DateOnly? OutDate,
    string Status,
    string DeliveryMethod,
    string Memo,
    int ProgressPercent,
    string CreatorName,
    DateTime CreateDate,
    string ModifierName,
    DateTime? ModifyDate
);

public record HandoverUpsertRequest(
    string Vendor,
    string Owner,
    string Content,
    DateOnly? InDate,
    DateOnly? OutDate,
    string DeliveryMethod,
    string Memo
);

public record HandoverStatusRequest(string Status);  // 진행 / 포장 / 완료
