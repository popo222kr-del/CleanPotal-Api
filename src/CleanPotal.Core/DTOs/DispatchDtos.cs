namespace CleanPotal.Core.DTOs;

public record DispatchDto(
    int Id, string VendorName, string OutgoingDetails, string IncomingDetails,
    string ManagerName, string ContactNumber, string FullAddress, string Note, DateTime CreateDate);

public record DispatchUpsertRequest(
    string VendorName, string OutgoingDetails, string IncomingDetails,
    string ManagerName, string ContactNumber, string FullAddress, string Note);

// ── 날짜별 배차표 (통합 배차표 화면) ──

/// <summary>배차표 한 행. Id=0 이면 신규.</summary>
public record DispatchRowRequest(
    int Id, string VendorName, string OutgoingDetails, string IncomingDetails,
    string ManagerName, string ContactNumber, string FullAddress, string Note);

/// <summary>해당 날짜의 배차표 전체를 한 번에 저장(추가/수정/삭제 동기화).</summary>
public record DispatchDayRequest(List<DispatchRowRequest> Rows);

/// <summary>배차 항목 이월(날짜 이동).</summary>
public record DispatchMoveRequest(DateOnly TargetDate);
