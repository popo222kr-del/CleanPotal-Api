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
/// <param name="KnownIds">
/// 이 화면이 불러왔던 그날의 행 번호. 서버는 이 중에서 요청에 빠진 행만 지운다 — 그 사이 다른 사람이 추가한 행은
/// 이 화면이 모르므로 지우지 않는다. 비어 있으면(옛 화면) 예전처럼 요청을 그날의 전체 상태로 본다.
/// </param>
public record DispatchDayRequest(List<DispatchRowRequest> Rows, List<int>? KnownIds = null);

/// <summary>배차 항목 이월(날짜 이동).</summary>
public record DispatchMoveRequest(DateOnly TargetDate);
