using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// TRAN 코드(공정 전이 규칙) 조회 구현. OPER 화면의 "TRAN 코드 선택" 드롭다운을 채운다.
// 지금 공정에서 쓸 수 있는 TRAN만 걸러 주므로, 작업자가 고를 수 있는 것 자체가 제한된다.
public class TranDefinitionService : ITranDefinitionService
{
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessTransitionDefinition, int> _transitions;
    private readonly IRepository<ReasonCode, int> _reasonCodes;

    public TranDefinitionService(
        IRepository<Lot, int> lots,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessTransitionDefinition, int> transitions,
        IRepository<ReasonCode, int> reasonCodes)
    {
        _lots = lots;
        _processDefinitions = processDefinitions;
        _transitions = transitions;
        _reasonCodes = reasonCodes;
    }

    public async Task<IReadOnlyList<TranOptionDto>> GetAvailableTransitionsAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

        var processes = await _processDefinitions.ListAllAsync(cancellationToken);
        var processByOperCode = processes.ToDictionary(p => p.OperCode);
        var currentOperCode = processes.First(p => p.Id == lot.CurrentProcessDefinitionId).OperCode;

        var candidates = await _transitions.ListAsync(
            t => t.SourceOperCode == currentOperCode && t.IsActive, cancellationToken);

        return candidates
            .Where(t => IsAllowed(t.TranCode, lot.CurrentStatus, candidates))
            .Select(t => new TranOptionDto(
                t.Id,
                t.TranId,
                t.Description,
                t.TranCode,
                t.TargetOperCode,
                t.TargetOperCode is { } targetOperCode && processByOperCode.TryGetValue(targetOperCode, out var targetProcess)
                    ? targetProcess.ProcessName
                    : null))
            .ToList();
    }

    public async Task<IReadOnlyList<ReasonCodeDto>> GetReasonCodesAsync(ReasonCategory category, CancellationToken cancellationToken = default)
    {
        var codes = await _reasonCodes.ListAsync(r => r.Category == category && r.IsActive, cancellationToken);
        return codes.Select(r => new ReasonCodeDto(r.Code, r.Description)).ToList();
    }

    // 이 OPER에 TranCode.Start 행이 하나라도 있으면(세정/건조/Bake/Laser처럼 레시피 기반으로 실제 "작업 시작 ->
    // 진행중" 단계가 필요한 공정) 작업 상태는 InProgress여야 END/HOLD/SKIP/SHIP을 실행할 수 있다. Start 행이
    // 없는 OPER(입고/입고검사/출하검사/포장/고객출하처럼 한 번의 TRAN으로 즉시 처리되는 공정)는 Waiting
    // 상태에서 바로 실행한다 - "기타프로그램 마스터 데이터.xlsx" TRAN 사용 공정 시트에서 OPER별로 START 행의
    // 유무가 갈리는 것을 그대로 반영한 규칙이다 (별도 플래그 없이 데이터 자체에서 유도).
    private static bool IsAllowed(TranCode tranCode, LotStatus currentStatus, IReadOnlyList<ProcessTransitionDefinition> candidates)
    {
        var hasStart = candidates.Any(c => c.TranCode == TranCode.Start);
        var workingStatus = hasStart ? LotStatus.InProgress : LotStatus.Waiting;

        return tranCode switch
        {
            TranCode.Start => currentStatus is LotStatus.Waiting or LotStatus.Rework,
            TranCode.End or TranCode.Skip or TranCode.Ship or TranCode.Hold => currentStatus == workingStatus,
            TranCode.Release => currentStatus == LotStatus.Hold,
            TranCode.Rework => currentStatus == workingStatus || currentStatus == LotStatus.Hold,
            _ => false
        };
    }
}
