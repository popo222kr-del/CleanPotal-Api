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
    bool IsNewUpdate,   // 현재 사용자가 미확인 + 24h 내 타인 등록/수정 → 빨간 점
    string Images,      // 첨부 이미지 JSON (base64 data URL 배열)
    int RowVersion,     // 저장 시 그대로 돌려보내면 서버가 동시 수정 충돌을 잡는다
    bool CanDelete      // 작성자 본인 또는 관리자 — 삭제 버튼 표시용 (수정은 등급 2 면 가능)
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
    string Status = "진행",
    string? Images = null,
    int? RowVersion = null   // 수정 시 불러올 때 받은 값. 비우면 동시 수정 검사를 건너뛴다.
);

public record HandoverStatusRequest(string Status);  // 진행 / 포장 / 완료
