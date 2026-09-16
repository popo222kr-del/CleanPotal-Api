using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 제품에 공정 플로우를 붙이고 떼는 구현. "셋업 > 제품 셋업"의 공정 플로우 영역이 쓴다.
// 부여된 플로우와 아직 부여할 수 있는 플로우를 나눠 돌려준다.
public class ProductFlowService : IProductFlowService
{
    private readonly IRepository<ProductProcessFlow, int> _productFlows;
    private readonly IRepository<ProcessRoute, int> _processRoutes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public ProductFlowService(
        IRepository<ProductProcessFlow, int> productFlows,
        IRepository<ProcessRoute, int> processRoutes,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _productFlows = productFlows;
        _processRoutes = processRoutes;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<ProcessRouteOptionDto>> GetAssignedFlowsAsync(int productId, CancellationToken cancellationToken = default)
    {
        var assignments = await _productFlows.ListAsync(f => f.ProductId == productId, cancellationToken);
        var routes = (await _processRoutes.ListAllAsync(cancellationToken)).ToDictionary(r => r.Id);

        return assignments
            .OrderBy(a => a.SortOrder)
            .Where(a => routes.ContainsKey(a.ProcessRouteId))
            .Select(a => routes[a.ProcessRouteId])
            .Select(r => new ProcessRouteOptionDto(r.Id, r.RouteCode, r.RouteName))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, ProcessRouteOptionDto>> GetProductRouteMapAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await _productFlows.ListAllAsync(cancellationToken);
        var routes = (await _processRoutes.ListAllAsync(cancellationToken)).ToDictionary(r => r.Id);

        return assignments
            .GroupBy(a => a.ProductId)
            .Select(g => g.OrderBy(a => a.SortOrder).First())
            .Where(a => routes.ContainsKey(a.ProcessRouteId))
            .ToDictionary(
                a => a.ProductId,
                a => new ProcessRouteOptionDto(routes[a.ProcessRouteId].Id, routes[a.ProcessRouteId].RouteCode, routes[a.ProcessRouteId].RouteName));
    }

    public async Task<IReadOnlyList<ProcessRouteOptionDto>> GetAvailableFlowsAsync(int productId, CancellationToken cancellationToken = default)
    {
        var assignedIds = (await _productFlows.ListAsync(f => f.ProductId == productId, cancellationToken))
            .Select(a => a.ProcessRouteId)
            .ToHashSet();

        var routes = await _processRoutes.ListAllAsync(cancellationToken);
        return routes
            .Where(r => r.IsActive && !assignedIds.Contains(r.Id))
            .OrderBy(r => r.RouteCode)
            .Select(r => new ProcessRouteOptionDto(r.Id, r.RouteCode, r.RouteName))
            .ToList();
    }

    public async Task AssignFlowAsync(int productId, int processRouteId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        if (await _productFlows.ExistsAsync(f => f.ProductId == productId && f.ProcessRouteId == processRouteId, cancellationToken))
        {
            return;
        }

        var assignments = await _productFlows.ListAsync(f => f.ProductId == productId, cancellationToken);
        var nextOrder = assignments.Count == 0 ? 1 : assignments.Max(a => a.SortOrder) + 1;

        await _productFlows.AddAsync(new ProductProcessFlow
        {
            ProductId = productId,
            ProcessRouteId = processRouteId,
            SortOrder = nextOrder
        }, cancellationToken);

        _auditLogger.Log("Product.AssignFlow", nameof(ProductProcessFlow), $"{productId}/{processRouteId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignFlowAsync(int productId, int processRouteId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var assignments = await _productFlows.ListAsync(f => f.ProductId == productId && f.ProcessRouteId == processRouteId, cancellationToken);
        var assignment = assignments.FirstOrDefault();
        if (assignment is null)
        {
            return;
        }

        _productFlows.Remove(assignment);
        _auditLogger.Log("Product.UnassignFlow", nameof(ProductProcessFlow), $"{productId}/{processRouteId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
