namespace ProductionManagement.Application.DTOs;

// 목록에서 LOT을 우클릭해 여는 "LOT 현황 조회" 팝업 창의 상단 정보 영역 - 실사용 MES 화면 참고
// (2026-08-18 피드백). RecipeId/ResId는 세정(3000)/건조(4000) 공정에 있을 때만 값이 채워진다.
public record LotHistoryHeaderDto(
    int LotId,
    string LotNumber,
    string SerialNumber,
    string? ExportNumber,
    string MatId,
    string MatDesc,
    string CustomerName,
    string? Line,
    int CurrentOperCode,
    string CurrentOperName,
    string? RecipeId,
    string? ResId,
    string? Comment,
    // 2026-08-31 피드백(#2): LOT 현황 조회 상단의 "매출 고객사"를 "PM RES ID"(전산등록 PM 설비명)로 대체.
    string? PmEquipmentName = null);

// "LOT 현황 조회" 팝업의 TRAN 이력 그리드 한 행. ProcessHistory 한 건(=한 시도)에서 StartedAt/CompletedAt이
// 다르면 START/END 두 행으로, 같으면(전산등록 등 즉시완료) 한 행으로 파생된다 (LotHistoryService 참고).
public record LotTransitionRowDto(
    int OperCode,
    string OperDesc,
    string TranCode,
    DateTime TranTime,
    int Quantity,
    string? RecipeId,
    string? RecipeDesc,
    string? ResId,
    string? TranCause,
    string? Comment,
    string UserId,
    string UserDesc);

// "LOT 현황 조회" 화면 좌측 하단 - 같은 S/N을 가진 LOT(=입고/전산등록 사이클)마다 한 행씩 파생된다
// (2026-08-19 피드백: "S/N 기준 제품에 대한 누적 이력, 헤더 - NO / 입고일자 / 출고일자"). OutboundDate는
// 아직 고객출하(OperCode 8100)가 완료되지 않았으면 null. Remarks는 이 사이클(=이 Lot) 동안 작업자가
// OPER 화면 CMT_AETS 칸에 남긴 코멘트를 시간순으로 모아 " / "로 이어붙인 것이다(2026-08-19 피드백:
// "비고 헤더를 추가... 작성했던 코멘트들이 보일 수 있게") - 코멘트가 하나도 없으면 null.
public record LotCycleRowDto(
    int No,
    DateTime InboundDate,
    DateTime? OutboundDate,
    string? Remarks);

// "LOT 현황 조회" 화면 우측 하단 - 같은 S/N을 가진 모든 LOT의 InspectionRecord(검사 파라미터 측정값)를
// 시간순으로 보여준다(2026-08-19 피드백: "파라미터 적용 값 표기"). 어느 OPER/시도에서 기록됐는지 알 수
// 있도록 OperCode/Desc도 함께 내려준다.
public record LotParameterRecordDto(
    int OperCode,
    string OperDesc,
    string ParameterCode,
    string ParameterDescription,
    string? InputValue,
    string? Comment,
    DateTime RecordedAt);
