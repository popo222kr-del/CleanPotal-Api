using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// TRAN CODE 전이 엔진(2026-08-18). OPER 화면에서 작업자가 선택한 TranId 하나로 Start/End/Hold/Release/
// Rework/Skip/Ship 7가지 동작 중 하나를 실행한다. 어떤 TRAN이 지금 유효한지는 ITranDefinitionService가
// 먼저 걸러주지만, 여기서도 반드시 다시 검증한다 (UI가 막았어도 서비스에서 재검증 - CLAUDE.md 8번).
public class OperActionService : IOperActionService
{
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<QuantityTransaction, int> _quantityTransactions;
    private readonly IRepository<Hold, int> _holds;
    private readonly IRepository<Rework, int> _reworks;
    private readonly IRepository<ProcessTransitionDefinition, int> _transitions;
    private readonly IRepository<ReasonCode, int> _reasonCodes;
    private readonly ITranDefinitionService _tranDefinitionService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly ICertificateFillService _certificateFillService;

    public OperActionService(
        IRepository<Lot, int> lots,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<QuantityTransaction, int> quantityTransactions,
        IRepository<Hold, int> holds,
        IRepository<Rework, int> reworks,
        IRepository<ProcessTransitionDefinition, int> transitions,
        IRepository<ReasonCode, int> reasonCodes,
        ITranDefinitionService tranDefinitionService,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        ICertificateFillService certificateFillService)
    {
        _lots = lots;
        _processDefinitions = processDefinitions;
        _processHistories = processHistories;
        _quantityTransactions = quantityTransactions;
        _holds = holds;
        _reworks = reworks;
        _transitions = transitions;
        _reasonCodes = reasonCodes;
        _tranDefinitionService = tranDefinitionService;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _certificateFillService = certificateFillService;
    }

    public async Task ExecuteTranAsync(OperExecuteTranRequest request, CancellationToken cancellationToken = default)
    {
        var lot = await GetLotOrThrowAsync(request.LotId, cancellationToken);
        var transition = await _transitions.GetByIdAsync(request.TransitionId, cancellationToken)
            ?? throw new InvalidOperationException("TRAN 정의를 찾을 수 없습니다.");

        if (!transition.IsActive)
        {
            throw new InvalidProcessTransitionException("비활성화된 TRAN입니다.");
        }

        // Lot이 실제 이 TRAN의 출발 OPER에 있고, 현재 상태에서 이 TRAN이 유효한지는
        // ITranDefinitionService의 필터링 규칙을 그대로 다시 태워서 검증한다 (규칙 이중 관리 방지).
        var available = await _tranDefinitionService.GetAvailableTransitionsAsync(request.LotId, cancellationToken);
        if (!available.Any(a => a.TransitionId == request.TransitionId))
        {
            throw new InvalidProcessTransitionException("현재 상태에서 실행할 수 없는 TRAN입니다.");
        }

        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        // 2026-08-31 피드백(#12): 배치 대표 LOT의 TRAN은 묶인 멤버들에도 함께 적용해 같이 이동시킨다.
        // (예전엔 대표만 이동하고 멤버는 입고검사 등 원래 OPER에 그대로 남아 있어, UnBatch 시 멤버가
        //  뒤처진 것처럼 보이고 이력도 없던 문제). 대표와 같은 OPER에 있는 멤버만 대상으로 한다(정합성).
        var repOperBefore = lot.CurrentProcessDefinitionId;
        var members = (await _lots.ListAsync(m => m.RepresentativeLotId == lot.Id, cancellationToken))
            .Where(m => m.CurrentProcessDefinitionId == repOperBefore)
            .ToList();
        var group = new List<Lot> { lot };
        group.AddRange(members);

        foreach (var member in group)
        {
            await ApplyTranToLotAsync(member, transition, request, actor, now, cancellationToken);
        }

        _auditLogger.Log("Oper.ExecuteTran", nameof(Lot), lot.LotNumber, actor,
            $"TranId={transition.TranId}, TranCode={transition.TranCode}" + (members.Count > 0 ? $", BatchMembers={members.Count}" : string.Empty));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 2026-08-31 피드백(#10): 성적서는 입고검사(2100)·출고검사(7000)를 "완료(End)"할 때만 값을 반영한다
        // (공정 이동마다 열고 닫지 않는다). 검사값은 이미 저장돼 있으므로 커밋 후 채운다. 실패해도 무시.
        if (transition.TranCode == TranCode.End && transition.SourceOperCode is 2100 or 7000)
        {
            foreach (var member in group)
            {
                await _certificateFillService.FillAsync(member.Id, cancellationToken);
            }
        }
    }

    // 한 LOT에 TRAN을 적용한다(배치면 대표+멤버 각각에 같은 TRAN을 적용해 함께 이동시킨다).
    private async Task ApplyTranToLotAsync(Lot lot, ProcessTransitionDefinition transition, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        switch (transition.TranCode)
        {
            case TranCode.Start:
                await ExecuteStartAsync(lot, actor, now, cancellationToken);
                break;
            case TranCode.End:
                await ExecuteEndAsync(lot, transition, request, actor, now, cancellationToken);
                break;
            case TranCode.Skip:
                await ExecuteSkipAsync(lot, transition, request, actor, now, cancellationToken);
                break;
            case TranCode.Hold:
                await ExecuteHoldAsync(lot, request, actor, now, cancellationToken);
                break;
            case TranCode.Release:
                await ExecuteReleaseAsync(lot, request, actor, now, cancellationToken);
                break;
            case TranCode.Rework:
                await ExecuteReworkAsync(lot, transition, request, actor, now, cancellationToken);
                break;
            case TranCode.Ship:
                await ExecuteShipAsync(lot, request, actor, now, cancellationToken);
                break;
        }

        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;
        _lots.Update(lot);
    }

    private async Task ExecuteStartAsync(Lot lot, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var previousAttempts = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);

        // 작업시작을 누르기 전 설비명(RES ID)/코멘트를 먼저 기입해뒀다면 InspectionService.SaveAsync가 이미
        // Waiting 상태의 이력을 하나 열어 그 값을 담아뒀다 - 여기서 무조건 새 이력을 또 만들면 그 값이 버려
        // 지므로(2026-08-19 버그: 설비명 기입 후 TRAN 실행 시 값이 사라짐), 아직 안 끝난(CompletedAt == null)
        // 이력이 있으면 새로 만들지 않고 그대로 재사용한다.
        var open = previousAttempts
            .Where(h => h.CompletedAt == null)
            .OrderByDescending(h => h.StartedAt)
            .FirstOrDefault();
        if (open is not null)
        {
            open.Worker = actor;
            open.StartedAt = now;
            open.Status = LotStatus.InProgress;
            lot.CurrentStatus = LotStatus.InProgress;
            return;
        }

        // 새로 이력을 여는 경우, 이 Lot이 같은 OPER를 이전에 시도한 적이 있다면(재시도) 그때 실제 썼던
        // 설비호기/레시피를 기본값으로 이어받는다 - 매 시도마다 다시 기입하지 않아도 되게 한다.
        var previous = previousAttempts.OrderByDescending(h => h.StartedAt).FirstOrDefault();

        await _processHistories.AddAsync(new ProcessHistory
        {
            Lot = lot,
            ProcessDefinitionId = lot.CurrentProcessDefinitionId,
            Worker = actor,
            StartedAt = now,
            Status = LotStatus.InProgress,
            Quantity = lot.ReceivedQuantity,
            AttemptNumber = previousAttempts.Count + 1,
            EquipmentId = previous?.EquipmentId,
            RecipeDefinitionId = previous?.RecipeDefinitionId
        }, cancellationToken);

        lot.CurrentStatus = LotStatus.InProgress;
    }

    private async Task ExecuteEndAsync(Lot lot, ProcessTransitionDefinition transition, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var openHistory = await GetOrOpenHistoryAsync(lot, actor, now, cancellationToken);
        openHistory.CompletedAt = now;
        openHistory.Status = LotStatus.Completed;
        openHistory.Result = ProcessResult.Pass;
        openHistory.DefectQuantity = request.DefectQuantity;
        openHistory.Remarks = request.Remarks;

        if (request.DefectQuantity > 0)
        {
            await _quantityTransactions.AddAsync(new QuantityTransaction
            {
                Lot = lot,
                TransactionType = QuantityTransactionType.Defect,
                Quantity = request.DefectQuantity,
                OccurredAt = now,
                RecordedBy = actor,
                Remarks = request.Remarks
            }, cancellationToken);
        }

        // TargetOperCode는 End에서 항상 값이 있다 (전체 46행 중 TargetOperCode가 null인 건 T530 Ship 하나뿐).
        var targetProcess = await GetProcessByOperCodeOrThrowAsync(transition.TargetOperCode!.Value, cancellationToken);
        lot.CurrentProcessDefinitionId = targetProcess.Id;
        lot.CurrentStatus = LotStatus.Waiting;
    }

    private async Task ExecuteSkipAsync(Lot lot, ProcessTransitionDefinition transition, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var reason = await ValidateReasonCodeAsync(ReasonCategory.Skip, request.ReasonCode, cancellationToken);

        var openHistory = await GetOrOpenHistoryAsync(lot, actor, now, cancellationToken);
        openHistory.CompletedAt = now;
        openHistory.Status = LotStatus.Completed;
        openHistory.Result = ProcessResult.Fail;
        openHistory.DefectQuantity = request.DefectQuantity;
        openHistory.Remarks = FormatReasonRemarks(reason, request.Remarks);

        if (request.DefectQuantity > 0)
        {
            await _quantityTransactions.AddAsync(new QuantityTransaction
            {
                Lot = lot,
                TransactionType = QuantityTransactionType.Defect,
                Quantity = request.DefectQuantity,
                OccurredAt = now,
                RecordedBy = actor,
                Remarks = FormatReasonRemarks(reason, request.Remarks)
            }, cancellationToken);
        }

        var targetProcess = await GetProcessByOperCodeOrThrowAsync(transition.TargetOperCode!.Value, cancellationToken);
        lot.CurrentProcessDefinitionId = targetProcess.Id;
        lot.CurrentStatus = LotStatus.Waiting;
    }

    private async Task ExecuteHoldAsync(Lot lot, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var reason = await ValidateReasonCodeAsync(ReasonCategory.Hold, request.ReasonCode, cancellationToken);

        var openHistory = await GetOrOpenHistoryAsync(lot, actor, now, cancellationToken);
        openHistory.CompletedAt = now;
        openHistory.Status = LotStatus.Hold;
        openHistory.Remarks = FormatReasonRemarks(reason, request.Remarks);

        await _holds.AddAsync(new Hold
        {
            Lot = lot,
            ProcessDefinitionId = lot.CurrentProcessDefinitionId,
            RaisedBy = actor,
            RaisedAt = now,
            Reason = FormatReasonRemarks(reason, request.Remarks),
            IsReleased = false
        }, cancellationToken);

        lot.CurrentStatus = LotStatus.Hold;
    }

    private async Task ExecuteReleaseAsync(Lot lot, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var reason = await ValidateReasonCodeAsync(ReasonCategory.Release, request.ReasonCode, cancellationToken);

        var openHolds = await _holds.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId && !h.IsReleased, cancellationToken);
        var hold = openHolds.OrderByDescending(h => h.RaisedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("해제할 HOLD 이력이 없습니다.");

        hold.IsReleased = true;
        hold.ReleasedBy = actor;
        hold.ReleasedAt = now;
        hold.ReleaseReason = FormatReasonRemarks(reason, request.Remarks);

        // 이 OPER에 작업시작(Start) TRAN이 있는 공정이면 HOLD 해제 즉시 작업을 재개할 수 있도록 진행중 이력을
        // 새로 열어준다(다시 작업시작을 누르지 않아도 됨). Start가 없는 즉시처리형 OPER는 대기 상태로 되돌린다.
        var hasStart = await HasStartTransitionAsync(lot.CurrentProcessDefinitionId, cancellationToken);
        if (hasStart)
        {
            var previousAttempts = await _processHistories.ListAsync(
                h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
            // HOLD로 닫힌 직전 이력에 기입돼 있던 설비호기/레시피를 그대로 이어받는다 - 그렇지 않으면 HOLD
            // 해제 직후 값이 비어 보여 다시 기입해야 하는 것처럼 보인다(2026-08-19 버그 수정, ExecuteStartAsync
            // 와 동일한 이유).
            var previous = previousAttempts.OrderByDescending(h => h.StartedAt).FirstOrDefault();
            await _processHistories.AddAsync(new ProcessHistory
            {
                Lot = lot,
                ProcessDefinitionId = lot.CurrentProcessDefinitionId,
                Worker = actor,
                StartedAt = now,
                Status = LotStatus.InProgress,
                Quantity = lot.ReceivedQuantity,
                AttemptNumber = previousAttempts.Count + 1,
                EquipmentId = previous?.EquipmentId,
                RecipeDefinitionId = previous?.RecipeDefinitionId
            }, cancellationToken);
            lot.CurrentStatus = LotStatus.InProgress;
        }
        else
        {
            lot.CurrentStatus = LotStatus.Waiting;
        }
    }

    private async Task ExecuteReworkAsync(Lot lot, ProcessTransitionDefinition transition, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var reason = await ValidateReasonCodeAsync(ReasonCategory.Rework, request.ReasonCode, cancellationToken);

        var openHistories = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId && h.CompletedAt == null, cancellationToken);
        var openHistory = openHistories.OrderByDescending(h => h.StartedAt).FirstOrDefault();
        if (openHistory is not null)
        {
            openHistory.CompletedAt = now;
            openHistory.Status = LotStatus.Rework;
            openHistory.Result = ProcessResult.Fail;
            openHistory.Remarks = FormatReasonRemarks(reason, request.Remarks);
        }

        var previousAttempts = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);

        await _reworks.AddAsync(new Rework
        {
            Lot = lot,
            ProcessDefinitionId = lot.CurrentProcessDefinitionId,
            AttemptNumber = previousAttempts.Count + 1,
            Reason = FormatReasonRemarks(reason, request.Remarks),
            DecidedBy = actor,
            DecidedAt = now
        }, cancellationToken);

        // TargetOperCode는 같은 공정(재세정 등 제자리 재작업)일 수도, 이전 공정(예: 4000 -> 3000)일 수도 있다.
        // "뒤로 가기는 관리자 Rollback만 허용" 규칙은 TRAN으로 발생하는 정상 역행에는 더 이상 적용하지 않는다
        // (Rollback은 별도 "이력 삭제" 탭으로 재편될 예정 - 사용자 확정, 2026-08-18).
        var targetProcess = await GetProcessByOperCodeOrThrowAsync(transition.TargetOperCode!.Value, cancellationToken);
        lot.CurrentProcessDefinitionId = targetProcess.Id;
        lot.CurrentStatus = LotStatus.Rework;
    }

    private async Task ExecuteShipAsync(Lot lot, OperExecuteTranRequest request, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var reason = await ValidateReasonCodeAsync(ReasonCategory.Ship, request.ReasonCode, cancellationToken);

        var openHistory = await GetOrOpenHistoryAsync(lot, actor, now, cancellationToken);
        openHistory.CompletedAt = now;
        openHistory.Status = LotStatus.Completed;
        openHistory.Result = ProcessResult.Pass;
        openHistory.Remarks = FormatReasonRemarks(reason, request.Remarks);

        // 고객출하(SHIP)는 더 넘어갈 다음 OPER이 없는 종결 처리 - Lot은 8100에 남고 상태만 Completed로
        // 바뀐다. OPER 목록 조회(EfLotQueryRepository)는 Completed Lot을 기본 화면에서 제외해 "출하 처리됨"을
        // 반영하지만, ProcessHistory/QuantityTransaction은 지우지 않고 계속 누적되어 이력 조회에서는 그대로
        // 확인할 수 있다 (사용자 지시, 2026-08-18: "OPER 목록에서는 출하 처리, 이력 조회는 누적").
        lot.CurrentStatus = LotStatus.Completed;

        await _quantityTransactions.AddAsync(new QuantityTransaction
        {
            Lot = lot,
            TransactionType = QuantityTransactionType.Shipped,
            Quantity = lot.ReceivedQuantity,
            OccurredAt = now,
            RecordedBy = actor,
            Remarks = FormatReasonRemarks(reason, request.Remarks)
        }, cancellationToken);
    }

    // Start가 있는 공정(세정/건조/Bake/Laser)은 Start TRAN으로 열어둔 진행중 이력을 재사용/마감하고,
    // Start가 없는 즉시처리형 공정(입고/입고검사/출하검사/포장/고객출하)은 이 호출 시점에 이력을 새로 열고
    // 바로 마감한다 - TranDefinitionService의 상태 게이팅 규칙과 짝을 이루는 실행 측 규칙.
    //
    // 호출부에서 반환된 엔티티의 속성만 바꾸고 별도로 Update()를 부르지 않는 이유: EfRepository.ListAsync는
    // Tracking 상태로 조회하므로 속성 변경만으로 SaveChanges 시 자동 반영되고, 방금 AddAsync한 새 이력은
    // 아직 임시 키 상태라 Update()를 부르면 "Modified로 전환 불가" 예외가 난다(OperActionServiceTests에서
    // 실제로 재현됨) - EfRepository.cs의 ListAsync 주석과 같은 이유.

    private async Task<ProcessHistory> GetOrOpenHistoryAsync(Lot lot, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var histories = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId && h.CompletedAt == null, cancellationToken);
        var open = histories.OrderByDescending(h => h.StartedAt).FirstOrDefault();
        if (open is not null)
        {
            return open;
        }

        var previousAttempts = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
        var created = new ProcessHistory
        {
            Lot = lot,
            ProcessDefinitionId = lot.CurrentProcessDefinitionId,
            Worker = actor,
            StartedAt = now,
            Status = LotStatus.InProgress,
            Quantity = lot.ReceivedQuantity,
            AttemptNumber = previousAttempts.Count + 1
        };
        await _processHistories.AddAsync(created, cancellationToken);
        return created;
    }

    private async Task<bool> HasStartTransitionAsync(int processDefinitionId, CancellationToken cancellationToken)
    {
        var process = await GetProcessDefinitionOrThrowAsync(processDefinitionId, cancellationToken);
        var candidates = await _transitions.ListAsync(
            t => t.SourceOperCode == process.OperCode && t.IsActive && t.TranCode == TranCode.Start, cancellationToken);
        return candidates.Count > 0;
    }

    private async Task<ReasonCode> ValidateReasonCodeAsync(ReasonCategory category, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException(new[] { "사유 코드를 선택하세요." });
        }

        var matches = await _reasonCodes.ListAsync(
            r => r.Category == category && r.Code == code && r.IsActive, cancellationToken);
        return matches.FirstOrDefault()
            ?? throw new ValidationException(new[] { "유효하지 않은 사유 코드입니다." });
    }

    private static string FormatReasonRemarks(ReasonCode reason, string? remarks)
        => remarks is { Length: > 0 } ? $"{reason.Description}: {remarks}" : reason.Description;

    private async Task<Lot> GetLotOrThrowAsync(int lotId, CancellationToken cancellationToken)
        => await _lots.GetByIdAsync(lotId, cancellationToken) ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

    private async Task<ProcessDefinition> GetProcessDefinitionOrThrowAsync(int processDefinitionId, CancellationToken cancellationToken)
        => await _processDefinitions.GetByIdAsync(processDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("공정 정의를 찾을 수 없습니다.");

    private async Task<ProcessDefinition> GetProcessByOperCodeOrThrowAsync(int operCode, CancellationToken cancellationToken)
    {
        var all = await _processDefinitions.ListAsync(p => p.OperCode == operCode, cancellationToken);
        return all.FirstOrDefault() ?? throw new InvalidOperationException($"OPER {operCode}에 해당하는 공정 정의가 없습니다.");
    }
}
