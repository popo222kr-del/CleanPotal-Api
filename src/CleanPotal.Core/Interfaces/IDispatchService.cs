using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>배차 관리 (인수인계·자재물류 배차 원천).</summary>
public interface IDispatchService
{
    Task<IReadOnlyList<DispatchDto>> GetAllAsync(string? search);
    Task<DispatchDto> CreateAsync(DispatchUpsertRequest req);
    Task<DispatchDto?> UpdateAsync(int id, DispatchUpsertRequest req);
    Task<bool> DeleteAsync(int id);

    // ── 날짜별 배차표 ──
    Task<IReadOnlyList<DispatchDto>> GetByDateAsync(DateOnly date);
    Task<IReadOnlyList<DispatchDto>> SaveDayAsync(DateOnly date, IReadOnlyList<DispatchRowRequest> rows);
    Task<DispatchDto?> MoveAsync(int id, DateOnly targetDate);
}
