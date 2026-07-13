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
    bool IsWeekly,
    int ProgressPercent,
    string CreatorName,
    DateTime CreateDate,
    string ModifierName,
    DateTime? ModifyDate,
    bool IsNewUpdate   // 현재 사용자가 미확인 + 24h 내 타인 등록/수정 → 빨간 점
);

public record HandoverUpsertRequest(
    string Vendor,
    string Owner,
    string Content,
    DateOnly? InDate,
    DateOnly? OutDate,
    string DeliveryMethod,
    string Memo,
    bool IsWeekly = false,
    string Status = "진행"
);

public record HandoverStatusRequest(string Status);  // 진행 / 포장 / 완료
