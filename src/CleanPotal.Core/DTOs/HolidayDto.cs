namespace CleanPotal.Core.DTOs;

public record HolidayDto(DateOnly Date, string Name);

/// <summary>공휴일 관리 화면의 한 줄. Source: builtin(기본) · added(추가) · renamed(이름 변경) · removed(평일로 되돌림).</summary>
public record HolidayManageRowDto(DateOnly Date, string Name, string Source, string? BuiltInName, string? UpdatedBy, DateTime? UpdatedAt);

public record HolidayManagePageDto(int Year, bool HasBuiltIn, IReadOnlyList<HolidayManageRowDto> Rows);

/// <summary>IsOff=true 면 그날을 Name 으로 쉬는 날로, false 면 기본 목록의 공휴일을 평일로 되돌린다.</summary>
public record HolidaySaveRequest(DateOnly Date, string? Name, bool IsOff);
