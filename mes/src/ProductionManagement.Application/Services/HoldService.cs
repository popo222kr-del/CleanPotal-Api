using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Application.Services;

// HOLD 조회·해제 구현. 해제할 때는 사유와 조치 내용을 반드시 남기고, LOT을 HOLD 직전 상태로 되돌린다.
// HOLD를 거는 쪽은 OPER 화면의 TRAN 실행이라 여기에는 없다.
public class HoldService : IHoldService
{
    private readonly IRepository<Hold, int> _holds;
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;

    public HoldService(
        IRepository<Hold, int> holds,
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<ProcessDefinition, int> processDefinitions,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser)
    {
        _holds = holds;
        _lots = lots;
        _products = products;
        _processDefinitions = processDefinitions;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<HoldDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var holds = await _holds.ListAllAsync(cancellationToken);
        var lots = (await _lots.ListAllAsync(cancellationToken)).ToDictionary(l => l.Id);
        var products = (await _products.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);

        return holds
            .OrderByDescending(h => h.RaisedAt)
            .Select(h =>
            {
                lots.TryGetValue(h.LotId, out var lot);
                var productName = lot is not null && products.TryGetValue(lot.ProductId, out var product) ? product.ProductName : "-";
                var processName = processes.TryGetValue(h.ProcessDefinitionId, out var process) ? process.ProcessName : "-";

                return new HoldDto(
                    h.Id,
                    h.LotId,
                    lot?.LotNumber ?? "-",
                    productName,
                    processName,
                    h.RaisedBy,
                    h.RaisedAt,
                    h.Reason,
                    h.IsReleased,
                    h.ReleasedBy,
                    h.ReleasedAt,
                    h.ReleaseReason,
                    h.ActionTaken);
            })
            .ToList();
    }

    public async Task ReleaseAsync(HoldReleaseRequest request, CancellationToken cancellationToken = default)
    {
        var hold = await _holds.GetByIdAsync(request.HoldId, cancellationToken)
            ?? throw new InvalidOperationException("HOLD 이력을 찾을 수 없습니다.");

        if (hold.IsReleased)
        {
            throw new ValidationException(new[] { "이미 해제된 HOLD입니다." });
        }

        if (string.IsNullOrWhiteSpace(request.ReleaseReason))
        {
            throw new ValidationException(new[] { "해제 사유를 입력하세요." });
        }

        var actor = _currentUser.GetCurrentUser();

        hold.IsReleased = true;
        hold.ReleasedBy = actor;
        hold.ApprovedBy = actor;
        hold.ReleasedAt = DateTime.Now;
        hold.ReleaseReason = request.ReleaseReason;
        hold.ActionTaken = request.ActionTaken;
        _holds.Update(hold);

        _auditLogger.Log("Hold.Release", nameof(Hold), hold.Id.ToString(), actor, $"ReleaseReason={request.ReleaseReason}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
