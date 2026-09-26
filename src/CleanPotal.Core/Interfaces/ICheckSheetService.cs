using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

/// <summary>QR 체크시트 — 구역 점검 화면, 결과 저장·제출, 현황, NG, 월간 리포트, 양식 관리.</summary>
public interface ICheckSheetService
{
    Task<CheckSheetDto?> GetSheetAsync(string zoneCode, DateOnly? date, string? shift, CheckActor actor);
    Task<CheckResultDto?> SaveResultAsync(string zoneCode, int itemId, CheckResultSaveRequest req, CheckActor actor);
    Task<CheckSheetDto> SubmitAsync(string zoneCode, CheckSubmitRequest req, CheckActor actor);

    Task<CheckStatusDto> GetStatusAsync(DateOnly? date);
    Task<IReadOnlyList<CheckNgDto>> GetNgsAsync(bool openOnly, string? line, DateOnly? from, DateOnly? to);
    Task<CheckNgDto> CloseNgAsync(int resultId, string? note, CheckActor actor);
    Task<CheckReportDto> GetReportAsync(string line, int year, int month);

    Task<IReadOnlyList<CheckZoneDto>> GetZonesAsync();
    Task<CheckZoneDto> SaveZoneAsync(CheckZoneDto dto);
    Task<bool> DeleteZoneAsync(int id);
    Task<IReadOnlyList<CheckItemDto>> GetItemsAsync();
    Task<CheckItemDto> SaveItemAsync(CheckItemDto dto, CheckActor actor);
    Task<bool> DeleteItemAsync(int id);
    Task<CheckImportResultDto> ImportAsync(CheckImportRequest req, CheckActor actor);
    Task<CheckImportResultDto> CopyLineAsync(CheckCopyLineRequest req, CheckActor actor);
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync();
    Task<IReadOnlyDictionary<string, string>> SaveSettingsAsync(IReadOnlyDictionary<string, string> values);
}
