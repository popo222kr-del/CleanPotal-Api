namespace CleanPotal.Core.DTOs;

/// <summary>차량 컬럼 정의 (고정 5대): 키 + 표시 라벨.</summary>
public record MaterialVehicleDto(string Key, string Label);

/// <summary>오전/오후 한 칸: 목적지&amp;근무 + 배정 차량 키 목록.</summary>
public record MaterialCellDto(string Destination, List<string> Vehicles);

/// <summary>담당자 한 명의 하루(오전/오후).</summary>
public record MaterialRowDto(string Person, MaterialCellDto Am, MaterialCellDto Pm);

/// <summary>자재물류 일정 현황 하루 전체.</summary>
public record MaterialDayDto(
    DateOnly Date,
    IReadOnlyList<string> Roster,
    IReadOnlyList<MaterialVehicleDto> Vehicles,
    IReadOnlyList<MaterialRowDto> Rows,
    string NoteAm,
    string NotePm);

/// <summary>배차표에서 불러오기 후보: 표시엔 주소, 실제 입력은 업체명만.</summary>
public record MaterialDestinationDto(string Name, string Address);

// ── 저장 요청 ──
public record MaterialCellInput(string Destination, List<string>? Vehicles);
public record MaterialRowInput(string Person, MaterialCellInput Am, MaterialCellInput Pm);
public record MaterialSaveRequest(List<MaterialRowInput> Rows, string? NoteAm, string? NotePm);

/// <summary>로스터 저장(추가/삭제/순서변경/이름수정 일괄): 이름을 순서대로.</summary>
public record MaterialRosterSaveRequest(List<string> Names);
