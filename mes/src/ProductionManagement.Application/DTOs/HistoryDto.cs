using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// 이력 조회 화면들이 쓰는 자료 묶음.
//   AuditLogDto / AuditLogSearchRequest  감사 로그 탭
//   ProcessHistoryItemFullDto 등          세정 이력·공정 이력 목록의 한 행
// 검사 파라미터는 제품마다 달라서 고정 속성이 아니라 이름-값 모음으로 실어 나른다 - 화면이 그것을
// 보고 열을 그때그때 만든다(2단 그룹 헤더).
public record AuditLogDto(
    int AuditLogId,
    DateTime OccurredAt,
    string Actor,
    string Action,
    string EntityName,
    string EntityId,
    string? Detail);

public record AuditLogSearchRequest(string? Keyword = null, DateTime? DateFrom = null, DateTime? DateTo = null, int Take = 200);

public record ProcessHistoryItemFullDto(
    int ProcessHistoryId,
    int LotId,
    string LotNumber,
    string ProductName,
    string CustomerName,
    string ProcessName,
    string Worker,
    DateTime StartedAt,
    DateTime? CompletedAt,
    LotStatus Status,
    ProcessResult? Result,
    int Quantity,
    int DefectQuantity,
    int AttemptNumber,
    string? Remarks,
    string? EquipmentId,
    // 2026-08-20 "이력 삭제"(TRAN 무효화) 화면/표시용 - ProcessHistory.IsVoided 그대로.
    bool IsVoided);

// "세정 이력 조회" 화면의 검색 카드 필드 그대로(2026-08-19 피드백): 업체(드롭다운) 외에는 전부 수기
// 입력 텍스트 매치다. 예전 통합 키워드(Keyword)/제품·공정 드롭다운(ProductId/ProcessDefinitionId)/
// 기간(DateFrom/DateTo)은 이 화면에서 더 이상 안 써서 없앴다 - 필요해지면 다시 추가.
public record ProcessHistorySearchRequest(
    int? CustomerId = null,
    string? CleaningCode = null,
    string? ItemCode = null,
    string? ExportNumber = null,
    string? ProductName = null,
    string? SerialNumber = null,
    string? ProcessName = null,
    ProcessResult? Result = null,
    // 2026-08-21 "이력 삭제" 화면 검색바를 LOT 단일 필드로 단순화하며 추가.
    string? LotNumber = null,
    // 2026-09-03 피드백: 세정 이력 조회에 "입고일" 기준 기간 필터 추가(지정 일자 내 입고된 LOT만).
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    int Take = 200);
