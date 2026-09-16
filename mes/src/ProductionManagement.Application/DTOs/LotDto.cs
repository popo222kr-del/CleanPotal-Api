using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// SerialNumber는 전산등록 시 자동채번된 LOT 단위 S/N(Lot.SerialNumber - 반출번호_순번 형식)이다.
// Product.SerialNumber(제품 마스터의 선택적 정적 필드)와는 다른 값이니 혼동하지 않는다.
// ExportNumber/Line/ProcessLabel은 이 LOT을 만든 전산등록 레코드에서 가져오며, Registration을 거치지
// 않고 생성된(예: 구 LotService.CreateAsync 경로) LOT은 null일 수 있다.
// IsBatch: 대표 LOT으로 묶여있거나(RepresentativeLotId != null) 다른 LOT의 대표인 경우 true.
// StageArrivedAt: 현재 공정(CurrentProcessDefinitionId)에 도달한 시각 - Lot.UpdatedAt을 그대로 쓴다
// (공정 이동/S-N 수정/대표LOT 지정 등 Lot이 바뀔 때마다 갱신되므로 완벽히 "공정 이동 시각"만은 아니지만,
// 대부분의 경우 마지막 공정 이동 시각과 일치한다 - 별도 이력 컬럼을 새로 만들지 않고 재사용).
// TatHours: 직전 단계 체류시간(고정값) - ProcessHistory 기준 "현재 공정 진입 시각" - "직전 공정 진입 시각".
// 둘 중 하나라도 이력이 없으면(예: 전산등록 직후 첫 단계) null.
public record LotListItemDto(
    int LotId,
    string LotNumber,
    string ProductCode,
    string ItemCode,
    string ProductName,
    string SerialNumber,
    string? CleaningCode,
    string CustomerName,
    int Quantity,
    string CurrentProcessName,
    LotStatus CurrentStatus,
    bool IsBatch,
    DateTime ReceivedDate,
    DateTime StageArrivedAt,
    DateTime UpdatedAt,
    string? ExportNumber,
    string? Line,
    string? ProcessLabel,
    double? TatHours,
    // 2026-08-20 "입·출고 현황 조회" 헤더를 OperView "공정 목록"과 동일하게 맞추기 위해 추가 - 의미는
    // OperLotItemDto의 같은 이름 필드와 동일(RecipeCode: 세정/건조 등 레시피 배정된 공정에서만 값 있음,
    // Worker/StartedAt: 현재 공정의 최신 ProcessHistory 기준).
    string? RecipeCode,
    string? EquipmentId,
    string? Worker,
    DateTime? StartedAt,
    // 2026-08-25: "입 · 출고 현황 조회" 목록의 Comment 컬럼용 - LOT 공정 이력 중 가장 최근 비어있지 않은
    // 코멘트(Remarks). HOLD/재작업/특이사항 등 작업 중 남긴 메모가 여기 담긴다.
    string? Comment = null,
    // 2026-09-03 피드백: "입·출고 현황" 목록의 "업체명" 컬럼 - 매출 고객사가 아니라 전산등록의 PM 설비명
    // (OPER/대시보드의 "업체명"과 동일 데이터).
    string? PmEquipmentName = null);

// 2026-09-08 지시: "세정 이력 조회" 결과 그리드 전용 행. 기존 LotListItemDto는 여러 화면이 함께 쓰고
// 있어(입·출고 현황 등) 이 화면에서만 필요한 항목을 따로 담는다.
//
// - InitialSerialNumber(반출 S/N): 전산등록 시점 S/N. 기존 데이터에는 없어 null일 수 있다.
// - SerialNumber(반입 S/N): 현재 S/N.
// - PmEquipmentName(업체명), TeamName(분임조), ItemCategory(품목 구분): 전산등록/제품 마스터 값.
// - UsageCount(사용횟수): 같은 S/N으로 입고된 LOT 수(누적 입고 횟수).
// ※ 2026-09-09 지시로 앞쪽 고정 "무게" 컬럼은 삭제했다 - 무게는 입고/출고 파라미터 블록에서 IN·FI로
//   나뉘어 보이므로 앞쪽에 한 번 더 요약해 둘 이유가 없어졌다(값은 Values의 IN_WEIGHT_0/FI_WEIGHT_0).
// - PassFail(합/부): 검사 결과 상태. 한 번이라도 Fail이면 "부"(출고에서 뒤집히지 않는다),
//   Fail이 없고 Pass가 있으면 "합", 검사 이력이 없으면 null.
// - ShipDate(AETS 출고일 = 고객출고일): 전산등록의 고객출고일.
// - Values(2026-09-09 2단계): 입고/출고 파라미터·검사 블록의 값. 열이 제품마다 달라 고정 속성으로 둘 수
//   없어 "열 키 → 값" 사전으로 내려준다(같이 내려주는 LotHistoryColumnGroupDto가 어떤 키가 있는지 알려준다).
//   화면은 Binding Path=Values[키] 로 읽는다. 조회 결과 전체에서 쓰이는 키는 값이 없어도 null로 채워
//   넣으므로, 사전에 없는 키를 그리는 일은 없다.
public record LotHistoryRowDto(
    int LotId,
    string LotNumber,
    string? ExportNumber,
    string? CleaningCode,
    string ProductName,
    string? InitialSerialNumber,
    string SerialNumber,
    string? Line,
    string? PmEquipmentName,
    string? TeamName,
    string? ItemCategory,
    int UsageCount,
    DateTime ReceivedDate,
    LotStatus CurrentStatus,
    string? PassFail,
    DateTime? ShipDate,
    Dictionary<string, string?>? Values = null);

// 2026-09-09 "세정 이력 조회" 2단계: 입고/출고 파라미터·검사 블록의 2단 그룹 헤더 정의.
// Group = 헤더 윗줄(예: "IN · Visual", "FI INSP"), Leaf = 아랫줄 한 칸(예: "CHIP", "DATE").
// 어떤 파라미터가 몇 개 열로 나뉘는지는 제품 마스터(ParameterDefinition)의 코드·정렬순서·ValueCount를
// 그대로 따른다 - 화면이나 조회 코드에서 열을 임의로 만들지 않는다(마스터 데이터 우선 원칙).
public record LotHistoryColumnLeafDto(string Key, string Title, double Width);

// IsOutbound: 출고(7000) 블록이면 true - 화면에서 입고/출고 밴드 색을 나눠 칠하는 데만 쓴다.
public record LotHistoryColumnGroupDto(string Title, bool IsOutbound, IReadOnlyList<LotHistoryColumnLeafDto> Leaves);

// 결과 행 + 그 결과에 필요한 동적 열 정의를 함께 내려준다(둘이 항상 짝이어야 화면이 어긋나지 않는다).
public record LotHistoryResultDto(
    IReadOnlyList<LotHistoryRowDto> Rows,
    IReadOnlyList<LotHistoryColumnGroupDto> ColumnGroups);

public record LotSearchRequest(
    string? Keyword = null,
    int? CustomerId = null,
    int? ProductId = null,
    int? ProcessDefinitionId = null,
    LotStatus? Status = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    int Skip = 0,
    int Take = 50);

public record LotCreateRequest(int ProductId, int Quantity, DateTime ReceivedDate, string? Remarks);

public record LotDetailHeaderDto(
    int LotId,
    string LotNumber,
    string ProductCode,
    string ItemCode,
    string ProductName,
    string? SerialNumber,
    string CustomerName,
    int Quantity,
    int CurrentProcessDefinitionId,
    string CurrentProcessName,
    LotStatus CurrentStatus,
    DateTime ReceivedDate);

public record ProcessTimelineStepDto(int StepOrder, string ProcessName, TimelineState State);

// 전체 공정 조회의 WIP 요약 카드. Completed/Cancelled/Void는 "진행중"이 아니므로 제외하고 센다.
// 여러 ProcessRoute(STANDARD/SIMPLE 등)에 걸쳐 같은 ProcessDefinition이 중복 나타나지 않도록
// ProcessDefinitionId 기준으로 이미 중복 제거된 목록이다 - 정렬 기준은 Route별 StepOrder가 아니라
// 전역 순서인 OperCode를 쓴다 (CLAUDE.md "ProcessRoute를 여러 개 다루게 된 이후로 반드시 확인할 것" 참고).
// AvgWaitHours: 지금 이 공정에 있는 LOT들이 평균적으로 얼마나 오래 머물러 있는지(=지금 시각 - 각 LOT의
// UpdatedAt 평균) - WIP가 0이면 null. IsBottleneck: DashboardSummaryDto.LongWait와 같은 기준
// (SystemSettings의 LongWait.{공정코드}.DelayHours)으로, 이 공정에 그 기준을 넘겨 대기 중인 LOT이
// 하나라도 있으면 true (Dashboard 공정별 WIP 카드의 "병목 여부" 표시용, 2026-08-19 리디자인).
public record ProcessWipDto(int ProcessDefinitionId, string ProcessName, int OperCode, int Count, double? AvgWaitHours, bool IsBottleneck);

// 2026-08-31 피드백(#3): 대시보드 KPI/WIP 카드 클릭 시 보여줄 제품 목록의 카테고리.
public enum DashboardLotCategory
{
    TodayReceived,
    InProgress,
    Hold,
    Rework,
    ShippingWaiting,
    LongWait,
    WipStage,
    // 2026-09-01 피드백(#10): 당일 고객출하 완료된 LOT("출하 완료" 카드).
    TodayShipped
}

public record ProcessHistoryItemDto(
    string ProcessName,
    string Worker,
    DateTime StartedAt,
    DateTime? CompletedAt,
    LotStatus Status,
    ProcessResult? Result,
    int Quantity,
    int DefectQuantity,
    int AttemptNumber,
    string? Remarks);

public record LotDetailDto(
    LotDetailHeaderDto Header,
    IReadOnlyList<ProcessTimelineStepDto> Timeline,
    IReadOnlyList<ProcessHistoryItemDto> History);
