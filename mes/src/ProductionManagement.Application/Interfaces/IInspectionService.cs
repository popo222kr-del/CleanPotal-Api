using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// OPER 화면 우측 "제품 정보 + IN INSP/FI INSP" 패널 전용 (2026-08-18 실사용 MES 화면 참고).
public interface IInspectionService
{
    Task<InspectionPanelDto> GetPanelAsync(int lotId, CancellationToken cancellationToken = default);
    Task SaveAsync(SaveInspectionPanelRequest request, CancellationToken cancellationToken = default);
}
