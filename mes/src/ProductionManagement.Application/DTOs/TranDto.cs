using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// TargetOperCode/TargetProcessName은 TranCode.Ship인 행(T530)에서만 null - 더 넘어갈 다음 OPER이 없다.
public record TranOptionDto(
    int TransitionId,
    string TranId,
    string Description,
    TranCode TranCode,
    int? TargetOperCode,
    string? TargetProcessName);

public record ReasonCodeDto(string Code, string Description);

// ReasonCode는 TranCode가 Hold/Release/Rework/Skip/Ship일 때만 필요 - OperActionService가 해당 사유코드
// 테이블(ReasonCategory)에 존재하는 Code인지 재검증한다.
public record OperExecuteTranRequest(int LotId, int TransitionId, string? ReasonCode, int DefectQuantity, string? Remarks);
