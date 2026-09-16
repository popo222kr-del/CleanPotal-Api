using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// "품목 공정 플로우 설정" 화면(부여된 플로우 / 추가 가능 플로우 두 목록 + 이동) 전용.
public interface IProductFlowService
{
    Task<IReadOnlyList<ProcessRouteOptionDto>> GetAssignedFlowsAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessRouteOptionDto>> GetAvailableFlowsAsync(int productId, CancellationToken cancellationToken = default);

    // 2026-08-27: 전산등록에서 플로우 컬럼을 없애고 "세정코드(제품)에 배정한 플로우"를 그대로 쓰기 위해,
    // 제품별 대표(첫 번째/SortOrder 최소) 플로우를 한 번에 가져온다. 배정 플로우가 없는 제품은 맵에 없다.
    Task<IReadOnlyDictionary<int, ProcessRouteOptionDto>> GetProductRouteMapAsync(CancellationToken cancellationToken = default);
    Task AssignFlowAsync(int productId, int processRouteId, CancellationToken cancellationToken = default);
    Task UnassignFlowAsync(int productId, int processRouteId, CancellationToken cancellationToken = default);
}
