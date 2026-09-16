namespace ProductionManagement.Application.DTOs;

// Dashboard 상단 KPI 카드 6종 + 최근 30일 완료 기준 평균 TAT.
// LongWait: CurrentStatus가 Waiting/InProgress이고 현재 공정 체류시간이 SystemSettings의
// LongWait.{공정코드}.DelayHours 기준을 넘은 LOT 수 (기준값이 없는 공정은 세지 않는다).
public record DashboardSummaryDto(
    int TodayReceived,
    int InProgress,
    int Hold,
    int Rework,
    int ShippingWaiting,
    int LongWait,
    double? AvgTatHours,
    int CompletedCount,
    // 2026-09-01 피드백(#10): 당일 고객출하 완료된 LOT 수(대시보드 "출하 완료" 카드).
    int TodayShipped = 0);

// TAT 조회 화면: 선택 기간에 고객출하 완료된 LOT들의 요약 지표 + 개별 목록.
// TAT(Turn-Around-Time)는 제품 입고(ReceivedDate)부터 고객출하 완료(ShippedAt)까지 전체 소요 시간.
public record TatReportDto(
    double? AvgHours,
    double? MaxHours,
    double? MinHours,
    int Count,
    IReadOnlyList<TatLotItemDto> Items);

public record TatLotItemDto(
    int LotId,
    string LotNumber,
    string CustomerName,
    string ProductName,
    string SerialNumber,
    DateTime ReceivedDate,
    DateTime ShippedAt,
    double TatHours);
