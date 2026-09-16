using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 공정 마스터와 공정 플로우(라우팅) 관리. "셋업 > 공정 관리" 화면이 쓴다.
// 공정 하나하나(입고/세정/출고검사...)와, 그것을 순서대로 엮은 플로우를 함께 다룬다.
public interface IProcessDefinitionService
{
    Task<IReadOnlyList<ProcessDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProcessDefinitionDto> CreateAsync(ProcessDefinitionUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ProcessDefinitionDto> UpdateAsync(int processDefinitionId, ProcessDefinitionUpsertRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int processDefinitionId, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessRouteOptionDto>> GetAllRoutesAsync(CancellationToken cancellationToken = default);

    // 공정 플로우(ProcessRoute) 관리 - "공정 관리" 탭 상단(2026-08-24 신설).
    Task<IReadOnlyList<ProcessRouteDetailDto>> GetAllRouteDetailsAsync(CancellationToken cancellationToken = default);
    Task<ProcessRouteDetailDto> CreateRouteAsync(ProcessRouteUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ProcessRouteDetailDto> UpdateRouteAsync(int processRouteId, ProcessRouteUpsertRequest request, CancellationToken cancellationToken = default);
    Task SetRouteActiveAsync(int processRouteId, bool isActive, CancellationToken cancellationToken = default);
}
