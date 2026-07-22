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
    DateTime CreatedAt,
    string RequestImages,
    string ActionImages
);

public record ProdReqUpsertRequest(
    DateOnly? RequestDate,
    DateOnly? DueDate,
    string Category,
    string Location,
    string RequestDetail,
    DateOnly? ActionDate,
    string ActionDetail,
    string Assignee,
    string? Status = null,          // 조치 모달에서 상태까지 함께 저장 (완료 시 완료일 자동)
    string? RequestImages = null,
    string? ActionImages = null
);

public record ProdReqStatusRequest(string Status);

/// <summary>미확인(새 요청) 개수.</summary>
public record ProdReqUnreadDto(int Count);

// ── 등록 옵션 (구분/세부 위치/요청 분류) 관리 ──
public record ProdReqCategoryDto(string Name, IReadOnlyList<string> Subs);
public record ProdReqOptionsDto(IReadOnlyList<ProdReqCategoryDto> Categories, IReadOnlyList<string> ReqTypes);
