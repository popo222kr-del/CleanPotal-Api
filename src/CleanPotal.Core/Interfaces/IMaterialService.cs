using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>자재물류 일정 현황 — 로스터 관리 + 하루 일정 저장/조회 + 배차 후보.</summary>
public interface IMaterialService
{
    /// <summary>고정 차량 5대 정의.</summary>
    IReadOnlyList<MaterialVehicleDto> Vehicles { get; }

    /// <summary>지정 날짜의 표(로스터 순서대로 행 구성) + 특이사항.</summary>
    Task<MaterialDayDto> GetDayAsync(DateOnly date);

    /// <summary>지정 날짜의 표/특이사항 일괄 저장(마지막 저장 우선).</summary>
    Task<MaterialDayDto> SaveDayAsync(DateOnly date, MaterialSaveRequest req);

    /// <summary>담당자 로스터(이름 목록, 순서대로).</summary>
    Task<IReadOnlyList<string>> GetRosterAsync();

    /// <summary>담당자 로스터 일괄 저장(추가/삭제/순서변경/이름수정).</summary>
    Task<IReadOnlyList<string>> SaveRosterAsync(MaterialRosterSaveRequest req);

    /// <summary>배차표에서 불러오기 후보(과거 목적지 + 업체) — 표시 주소 포함.</summary>
    Task<IReadOnlyList<MaterialDestinationDto>> GetDestinationsAsync();
}
