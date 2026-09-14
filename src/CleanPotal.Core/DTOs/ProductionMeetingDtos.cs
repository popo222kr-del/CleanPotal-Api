namespace CleanPotal.Core.DTOs;

public record ProductionMeetingDto(
    int Id,
    string Title,
    DateOnly MeetingDate,
    string DayContent,
    string NightContent,
    string OfficeMemo,
    string DayTeam,      // 해당 날짜 주간 팀 (예측)
    string NightTeam,    // 해당 날짜 야간 팀 (예측)
    string CreatorName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int RowVersion,    // 저장 시 그대로 돌려보내면 서버가 동시 수정 충돌을 잡는다
    bool CanDelete     // 작성자 본인 또는 관리자 — 삭제 버튼 표시용 (수정은 등급 2 면 가능)
);

public record ProductionMeetingUpsertRequest(
    string Title,
    DateOnly MeetingDate,
    string DayContent,
    string NightContent,
    string OfficeMemo,
    int? RowVersion = null   // 수정 시 불러올 때 받은 값. 비우면 동시 수정 검사를 건너뛴다.
);

/// <summary>월별 그룹 (좌측 목록용).</summary>
public record ProductionMeetingGroupDto(string MonthTitle, IReadOnlyList<ProductionMeetingDto> Reports);
