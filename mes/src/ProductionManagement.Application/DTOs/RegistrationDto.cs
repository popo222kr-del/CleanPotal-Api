namespace ProductionManagement.Application.DTOs;

// ExportNumberOverride/SerialNumberOverride를 비워두면 기존처럼 자동채번한다. 값을 채우면 그 값을
// 그대로 쓴다(수기 입력 요청 반영) - ExportNumberOverride는 유일성을 재검증하고, SerialNumberOverride는
// Quantity==1일 때만 적용한다(수량이 여럿이면 유닛별 S/N을 그리드 한 칸에서 구분할 수 없으므로, 그
// 경우는 자동채번 후 각 LOT을 "입·출고 관리"에서 개별 수정하는 기존 방식을 그대로 쓴다).
public record RegistrationCreateRequest(
    int CustomerId,
    int ProductId,
    int ProcessRouteId,
    string Line,
    string ProcessLabel,
    int Quantity,
    DateTime ShipDate,
    // 2026-08-21 신설 - LOT 번호 생성에 쓰이는 2자리 코드(예: SS/SA/SU). ILotNumberGenerator.NextAsync 참고.
    string LotCode,
    string? ExportNumberOverride = null,
    string? SerialNumberOverride = null,
    string PmEquipmentName = "",
    string TeamName = "",
    string OrderNumber = "");

public record RegistrationUpdateRequest(string ExportNumber, string Line, string ProcessLabel);

// OPER 화면 "LOT 정보 수정" 탭 전용 - 이 Lot을 만든 전산등록 레코드의 수정 가능한 필드만 내려준다.
public record RegistrationEditDto(int RegistrationId, string ExportNumber, string Line, string ProcessLabel);

public record CreatedLotDto(int LotId, string LotNumber, string SerialNumber);

public record RegistrationResultDto(
    int RegistrationId,
    string ExportNumber,
    IReadOnlyList<CreatedLotDto> CreatedLots);

// 대량 붙여넣기 등록(한 번에 여러 품목)의 행 단위 결과. 한 행이 실패해도 나머지 행은 계속 진행되도록
// 각 요청을 독립적으로 처리한다 - RowIndex로 화면의 어느 행이었는지 사용자에게 알려준다.
public record RegistrationBatchItemResultDto(
    int RowIndex,
    bool Success,
    string? ErrorMessage,
    RegistrationResultDto? Result);

