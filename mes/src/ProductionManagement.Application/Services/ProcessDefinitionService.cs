using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Validators;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 공정 마스터와 공정 플로우 관리 구현. 공정 코드와 OPER 번호의 중복을 막고,
// 플로우는 단계 순번이 겹치지 않게 정리해서 저장한다.
// 이미 LOT이 쓰고 있는 공정·플로우는 지우지 않고 중지만 한다.
public class ProcessDefinitionService : IProcessDefinitionService
{
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessRoute, int> _processRoutes;
    private readonly IRepository<ProcessRouteStep, int> _processRouteSteps;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public ProcessDefinitionService(
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessRoute, int> processRoutes,
        IRepository<ProcessRouteStep, int> processRouteSteps,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _processDefinitions = processDefinitions;
        _processRoutes = processRoutes;
        _processRouteSteps = processRouteSteps;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<ProcessDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var processes = await _processDefinitions.ListAllAsync(cancellationToken);
        return processes
            .OrderBy(p => p.ProcessCode)
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<ProcessRouteOptionDto>> GetAllRoutesAsync(CancellationToken cancellationToken = default)
    {
        var routes = await _processRoutes.ListAllAsync(cancellationToken);
        return routes
            .Where(r => r.IsActive)
            .OrderBy(r => r.RouteName)
            .Select(r => new ProcessRouteOptionDto(r.Id, r.RouteCode, r.RouteName))
            .ToList();
    }

    public async Task<ProcessDefinitionDto> CreateAsync(ProcessDefinitionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var errors = ProcessDefinitionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (await _processDefinitions.ExistsAsync(p => p.ProcessCode == request.ProcessCode, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 공정 코드입니다: {request.ProcessCode}" });
        }

        var process = new ProcessDefinition
        {
            ProcessCode = request.ProcessCode,
            ProcessName = request.ProcessName,
            IsActive = true
        };

        await _processDefinitions.AddAsync(process, cancellationToken);
        _auditLogger.Log("ProcessDefinition.Create", nameof(ProcessDefinition), process.ProcessCode, _currentUser.GetCurrentUser(),
            $"ProcessName={process.ProcessName}. 주의: 기본 공정 순서(Process Route)에는 자동으로 추가되지 않음");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(process);
    }

    public async Task<ProcessDefinitionDto> UpdateAsync(int processDefinitionId, ProcessDefinitionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var errors = ProcessDefinitionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var process = await _processDefinitions.GetByIdAsync(processDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("공정을 찾을 수 없습니다.");

        if (await _processDefinitions.ExistsAsync(p => p.ProcessCode == request.ProcessCode && p.Id != processDefinitionId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 공정 코드입니다: {request.ProcessCode}" });
        }

        process.ProcessCode = request.ProcessCode;
        process.ProcessName = request.ProcessName;

        _processDefinitions.Update(process);
        _auditLogger.Log("ProcessDefinition.Update", nameof(ProcessDefinition), process.ProcessCode, _currentUser.GetCurrentUser(),
            $"ProcessName={process.ProcessName}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(process);
    }

    public async Task SetActiveAsync(int processDefinitionId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var process = await _processDefinitions.GetByIdAsync(processDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("공정을 찾을 수 없습니다.");

        process.IsActive = isActive;
        _processDefinitions.Update(process);
        _auditLogger.Log(isActive ? "ProcessDefinition.Activate" : "ProcessDefinition.Deactivate", nameof(ProcessDefinition), process.ProcessCode, _currentUser.GetCurrentUser());

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessRouteDetailDto>> GetAllRouteDetailsAsync(CancellationToken cancellationToken = default)
    {
        var routes = await _processRoutes.ListAllAsync(cancellationToken);
        var allSteps = await _processRouteSteps.ListAllAsync(cancellationToken);
        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);

        return routes
            .OrderBy(r => r.RouteCode)
            .Select(r => new ProcessRouteDetailDto(
                r.Id,
                r.RouteCode,
                r.RouteName,
                r.IsActive,
                allSteps
                    .Where(s => s.ProcessRouteId == r.Id)
                    .OrderBy(s => s.StepOrder)
                    .Select(s => new ProcessRouteStepDetailDto(
                        s.StepOrder,
                        s.ProcessDefinitionId,
                        processes.TryGetValue(s.ProcessDefinitionId, out var p) ? p.ProcessCode : "-",
                        processes.TryGetValue(s.ProcessDefinitionId, out var p2) ? p2.ProcessName : "-"))
                    .ToList()))
            .ToList();
    }

    public async Task<ProcessRouteDetailDto> CreateRouteAsync(ProcessRouteUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var errors = ProcessRouteValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (await _processRoutes.ExistsAsync(r => r.RouteCode == request.RouteCode, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 플로우 코드입니다: {request.RouteCode}" });
        }

        var route = new ProcessRoute { RouteCode = request.RouteCode, RouteName = request.RouteName, IsActive = true };
        await _processRoutes.AddAsync(route, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await ReplaceRouteStepsAsync(route.Id, request.ProcessDefinitionIds, cancellationToken);

        _auditLogger.Log("ProcessRoute.Create", nameof(ProcessRoute), route.RouteCode, _currentUser.GetCurrentUser(),
            $"RouteName={route.RouteName}, Steps={request.ProcessDefinitionIds.Count}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return (await GetAllRouteDetailsAsync(cancellationToken)).First(r => r.ProcessRouteId == route.Id);
    }

    public async Task<ProcessRouteDetailDto> UpdateRouteAsync(int processRouteId, ProcessRouteUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var errors = ProcessRouteValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var route = await _processRoutes.GetByIdAsync(processRouteId, cancellationToken)
            ?? throw new InvalidOperationException("플로우를 찾을 수 없습니다.");

        if (await _processRoutes.ExistsAsync(r => r.RouteCode == request.RouteCode && r.Id != processRouteId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 플로우 코드입니다: {request.RouteCode}" });
        }

        route.RouteCode = request.RouteCode;
        route.RouteName = request.RouteName;
        _processRoutes.Update(route);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await ReplaceRouteStepsAsync(route.Id, request.ProcessDefinitionIds, cancellationToken);

        _auditLogger.Log("ProcessRoute.Update", nameof(ProcessRoute), route.RouteCode, _currentUser.GetCurrentUser(),
            $"RouteName={route.RouteName}, Steps={request.ProcessDefinitionIds.Count}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return (await GetAllRouteDetailsAsync(cancellationToken)).First(r => r.ProcessRouteId == route.Id);
    }

    public async Task SetRouteActiveAsync(int processRouteId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProcess, cancellationToken);

        var route = await _processRoutes.GetByIdAsync(processRouteId, cancellationToken)
            ?? throw new InvalidOperationException("플로우를 찾을 수 없습니다.");

        route.IsActive = isActive;
        _processRoutes.Update(route);
        _auditLogger.Log(isActive ? "ProcessRoute.Activate" : "ProcessRoute.Deactivate", nameof(ProcessRoute), route.RouteCode, _currentUser.GetCurrentUser());

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // 기존 단계를 전부 지우고 새 순서대로 다시 채운다 - 순서를 하나씩 비교해 diff하는 것보다 훨씬 단순하고,
    // (ProcessRouteId, StepOrder) unique 인덱스와 충돌하지 않도록 삭제를 먼저 커밋한 뒤 추가한다.
    private async Task ReplaceRouteStepsAsync(int processRouteId, IReadOnlyList<int> processDefinitionIds, CancellationToken cancellationToken)
    {
        var existingSteps = await _processRouteSteps.ListAsync(s => s.ProcessRouteId == processRouteId, cancellationToken);
        foreach (var step in existingSteps)
        {
            _processRouteSteps.Remove(step);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < processDefinitionIds.Count; i++)
        {
            await _processRouteSteps.AddAsync(new ProcessRouteStep
            {
                ProcessRouteId = processRouteId,
                ProcessDefinitionId = processDefinitionIds[i],
                StepOrder = i + 1
            }, cancellationToken);
        }
    }

    private static ProcessDefinitionDto ToDto(ProcessDefinition process) => new(process.Id, process.ProcessCode, process.ProcessName, process.OperCode, process.IsActive);
}
