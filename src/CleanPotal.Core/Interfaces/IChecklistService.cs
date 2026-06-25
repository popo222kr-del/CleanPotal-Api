using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IChecklistService
{
    IReadOnlyList<ZoneDto> GetZones();
    Task<IReadOnlyList<InspectionItemDto>> GetItemsAsync(string zone);
    Task<InspectionItemDto> AddItemAsync(InspectionItemRequest req);
    Task<bool> DeleteItemAsync(int id);

    Task<InspectionRecordDto> SubmitAsync(SubmitInspectionRequest req, string worker);
    Task<IReadOnlyList<InspectionRecordDto>> GetRecordsAsync(DateOnly date, string? zone);
}
