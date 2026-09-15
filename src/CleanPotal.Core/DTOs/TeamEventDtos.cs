namespace CleanPotal.Core.DTOs;

public record TeamEventDto(
    int Id,
    string RegisteredBy,
    DateOnly StartDate,
    DateOnly EndDate,
    string Content,
    string Detail,
    DateTime CreateDate,
    // 이 일정에 관련된 부서들. 생산회의처럼 여러 부서가 함께 들어갈 수 있다.
    // 비어 있으면 부서를 지정하지 않은 일정(= 부서 필터와 무관하게 항상 보인다).
    IReadOnlyList<CalendarDeptDto> Depts
);

public record TeamEventRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string Content,
    string Detail,
    // 관련 부서 Id 목록. 이름이 아니라 Id 로 묶어, 부서 이름을 바꿔도 일정이 따라온다.
    IReadOnlyList<int>? DeptIds = null
);
