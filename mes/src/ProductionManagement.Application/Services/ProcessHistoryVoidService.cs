using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 공정 이력 무효화(되돌리기) 구현. "트랜잭션 > 이력 삭제" 화면이 쓴다.
// 이력을 실제로 지우지 않고 무효 표시를 남긴 뒤 LOT을 그 직전 공정 상태로 되돌린다 - 잘못 눌러
// 다음 공정으로 넘어간 LOT을 수습하는 용도이며, 되돌린 사실 자체도 감사 로그에 남는다.
public class ProcessHistoryVoidService : IProcessHistoryVoidService
{
    // 고객출하(SHIP) 완료 이력은 QuantityTransaction까지 연쇄로 되돌려야 해서 이번 범위에서 제외한다
    // (2026-08-20 사용자 확정 - "작업자가 잘못 실행한 TRAN을 무효화"의 가장 흔한 시나리오인 작업시작/
    // 완료 실수만 다룬다).
    private const int ShippingOperCode = 8100;
    // 2026-08-28 피드백: 입고(2000)에서 이력 삭제 시 무효화 대신 전산(LOT) 자체를 삭제한다.
    private const int ReceivingOperCode = 2000;

    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<Document, int> _documents;
    private readonly IRepository<InspectionRecord, int> _inspectionRecords;
    private readonly IRepository<QuantityTransaction, int> _quantityTransactions;
    private readonly IRepository<Hold, int> _holds;
    private readonly IRepository<Rework, int> _reworks;
    private readonly IRepository<Registration, int> _registrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public ProcessHistoryVoidService(
        IRepository<ProcessHistory, int> processHistories,
        IRepository<Lot, int> lots,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<Document, int> documents,
        IRepository<InspectionRecord, int> inspectionRecords,
        IRepository<QuantityTransaction, int> quantityTransactions,
        IRepository<Hold, int> holds,
        IRepository<Rework, int> reworks,
        IRepository<Registration, int> registrations,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _processHistories = processHistories;
        _lots = lots;
        _processDefinitions = processDefinitions;
        _documents = documents;
        _inspectionRecords = inspectionRecords;
        _quantityTransactions = quantityTransactions;
        _holds = holds;
        _reworks = reworks;
        _registrations = registrations;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task VoidAsync(int processHistoryId, string reason, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.Rollback, cancellationToken);

        // 2026-08-31 피드백: 삭제 사유는 따로 받지 않고 버튼 클릭만으로 동작한다(사유는 감사로그용 기본값).
        var auditReason = string.IsNullOrWhiteSpace(reason) ? "이력 삭제(가장 최근 이력)" : reason.Trim();

        var target = await _processHistories.GetByIdAsync(processHistoryId, cancellationToken)
            ?? throw new InvalidOperationException("이력을 찾을 수 없습니다.");

        if (target.IsVoided)
        {
            throw new ValidationException(new[] { "이미 무효화된 이력입니다." });
        }

        var process = await _processDefinitions.GetByIdAsync(target.ProcessDefinitionId, cancellationToken);

        // 2026-08-31 피드백: "전체 삭제가 아니라 가장 최근 이력만 삭제". 입고(2000)에서 전산(LOT) 전체를
        // 지우던 특수 처리는 폐지했다 - 이제 어느 공정이든 그 LOT의 "가장 최근 이력 한 건"만 무효화한다.
        var lotHistories = await _processHistories.ListAsync(h => h.LotId == target.LotId && !h.IsVoided, cancellationToken);
        var ordered = lotHistories.OrderByDescending(h => h.StartedAt).ThenByDescending(h => h.Id).ToList();

        if (ordered.Count == 0 || ordered[0].Id != target.Id)
        {
            throw new ValidationException(new[] { "가장 최근 이력만 삭제할 수 있습니다." });
        }

        if (target.Status is LotStatus.Hold or LotStatus.Rework)
        {
            throw new ValidationException(new[] { "HOLD/재작업 이력은 이번 기능으로 무효화할 수 없습니다." });
        }

        if (process?.OperCode == ShippingOperCode)
        {
            throw new ValidationException(new[] { "고객출하 이력은 이번 기능으로 무효화할 수 없습니다." });
        }

        var lot = await _lots.GetByIdAsync(target.LotId, cancellationToken)
            ?? throw new InvalidOperationException("LOT을 찾을 수 없습니다.");

        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        target.IsVoided = true;
        target.VoidedAt = now;
        target.VoidedBy = actor;
        target.VoidReason = auditReason;
        _processHistories.Update(target);

        // LOT은 항상 target이 속한 OPER에 그대로 두고 Waiting으로 되돌린다 - "이전 이력의 OPER/상태로
        // 되돌린다"는 첫 구현은 잘못이었다: 완료(End/Skip) TRAN이 LOT을 다음 OPER로 넘길 때 그 다음
        // OPER의 "Waiting" 상태에 대응하는 ProcessHistory 행은 애초에 생성되지 않는다(작업시작을 눌러야
        // 비로소 행이 생긴다). 그래서 방금 누른 작업시작(Start)을 무효화할 때 "이전 이력"을 기준으로
        // 삼으면 그 이전 OPER의 완료 상태로 한 단계 더 되돌아가 버리는 버그가 있었다. 무효화는 "이
        // OPER에서 방금 한 동작 하나만 취소"하는 것이므로, 항상 같은 OPER의 Waiting으로 되돌리는 것이
        // Start/End/Skip 어느 TRAN을 무효화하든 일관되게 맞다.
        lot.CurrentProcessDefinitionId = target.ProcessDefinitionId;
        lot.CurrentStatus = LotStatus.Waiting;
        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;
        _lots.Update(lot);

        _auditLogger.Log("ProcessHistory.Void", nameof(ProcessHistory), lot.LotNumber, actor, $"Reason={auditReason}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // 입고(2000) 단계 LOT의 "이력 삭제" = 전산(LOT) 자체 삭제. FK 자식 레코드를 먼저 지우고, 이 전산등록에
    // 다른 LOT이 남지 않으면 전산등록 레코드도 삭제한다.
    private async Task DeleteReceivingLotAsync(int lotId, string reason, CancellationToken cancellationToken)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("LOT을 찾을 수 없습니다.");
        var actor = _currentUser.GetCurrentUser();
        var lotNumber = lot.LotNumber;

        foreach (var e in await _inspectionRecords.ListAsync(r => r.LotId == lotId, cancellationToken)) { _inspectionRecords.Remove(e); }
        foreach (var e in await _documents.ListAsync(d => d.LotId == lotId, cancellationToken)) { _documents.Remove(e); }
        foreach (var e in await _quantityTransactions.ListAsync(q => q.LotId == lotId, cancellationToken)) { _quantityTransactions.Remove(e); }
        foreach (var e in await _holds.ListAsync(h => h.LotId == lotId, cancellationToken)) { _holds.Remove(e); }
        foreach (var e in await _reworks.ListAsync(r => r.LotId == lotId, cancellationToken)) { _reworks.Remove(e); }
        foreach (var e in await _processHistories.ListAsync(h => h.LotId == lotId, cancellationToken)) { _processHistories.Remove(e); }

        var registrationId = lot.RegistrationId;
        _lots.Remove(lot);

        if (registrationId is int rid)
        {
            var siblings = await _lots.ListAsync(l => l.RegistrationId == rid && l.Id != lotId, cancellationToken);
            if (siblings.Count == 0)
            {
                var registration = await _registrations.GetByIdAsync(rid, cancellationToken);
                if (registration is not null) { _registrations.Remove(registration); }
            }
        }

        _auditLogger.Log("Lot.DeleteAtReceiving", nameof(Lot), lotNumber, actor, $"Reason={reason}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
