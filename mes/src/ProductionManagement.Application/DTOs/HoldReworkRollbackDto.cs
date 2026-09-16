using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// HOLD / 재작업 / 되돌리기(Rollback) 화면이 쓰는 자료 묶음.
//   HoldDto, HoldReleaseRequest  HOLD 목록과 해제 입력(해제 사유·조치 내용)
//   ReworkDto                    재작업 이력 한 건
// 세 가지 모두 "정상 흐름에서 벗어난 LOT"을 다루기에 한 파일에 모아 두었다.
public record HoldDto(
    int HoldId,
    int LotId,
    string LotNumber,
    string ProductName,
    string ProcessName,
    string RaisedBy,
    DateTime RaisedAt,
    string Reason,
    bool IsReleased,
    string? ReleasedBy,
    DateTime? ReleasedAt,
    string? ReleaseReason,
    string? ActionTaken);

public record HoldReleaseRequest(int HoldId, string ReleaseReason, string? ActionTaken);

public record ReworkDto(
    int ReworkId,
    int LotId,
    string LotNumber,
    string ProductName,
    string ProcessName,
    int AttemptNumber,
    string Reason,
    string DecidedBy,
    DateTime DecidedAt,
    string? Remarks);
