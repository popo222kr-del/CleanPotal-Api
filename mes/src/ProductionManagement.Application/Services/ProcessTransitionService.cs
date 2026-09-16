using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.BusinessRules;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 공정 전이 가능 여부 판정. "지금 이 LOT을 이 TRAN으로 보내도 되는가"만 답하고, 안 되면
// InvalidProcessTransitionException을 던진다.
// 판단 근거는 코드에 박힌 규칙이 아니라 ProcessTransitionDefinition(TRAN 코드) 마스터다 -
// 그래서 공정 흐름을 바꿀 때 코드가 아니라 마스터 데이터를 고친다.
public class ProcessTransitionService : IProcessTransitionService
{
    // 2026-09-03 피드백(#2): 특정 제품은 중간 공정을 건너뛸 수 있으나, 입고검사(2100)/출고검사(7000)는
    // 어떤 경우에도 건너뛸 수 없다. StepOrder만으로는 어떤 공정인지 알 수 없어 OperCode로 판정한다.
    private static readonly int[] NonSkippableOperCodes = { 2100, 7000 };

    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<ProcessRouteStep, int> _routeSteps;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;

    public ProcessTransitionService(
        IRepository<Lot, int> lots,
        IRepository<ProcessRouteStep, int> routeSteps,
        IRepository<ProcessDefinition, int> processDefinitions)
    {
        _lots = lots;
        _routeSteps = routeSteps;
        _processDefinitions = processDefinitions;
    }

    public async Task EnsureTransitionAllowedAsync(int lotId, int targetProcessDefinitionId, LotStatus targetStatus, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

        // Phase 14부터 ProcessRoute가 여러 개(STANDARD/SIMPLE 등)가 되므로, 전체 Step이 아니라
        // 이 Lot이 실제로 속한 Route의 Step만 봐야 한다 (같은 ProcessDefinition이라도 Route마다
        // StepOrder가 다를 수 있음).
        var steps = await _routeSteps.ListAsync(s => s.ProcessRouteId == lot.ProcessRouteId, cancellationToken);

        var currentStep = steps.FirstOrDefault(s => s.ProcessDefinitionId == lot.CurrentProcessDefinitionId)
            ?? throw new InvalidOperationException("현재 공정이 이 Lot의 Process Route에 정의되어 있지 않습니다.");

        var targetStep = steps.FirstOrDefault(s => s.ProcessDefinitionId == targetProcessDefinitionId)
            ?? throw new InvalidOperationException("대상 공정이 이 Lot의 Process Route에 정의되어 있지 않습니다.");

        if (!ProcessTransitionRules.IsAllowed(currentStep.StepOrder, lot.CurrentStatus, targetStep.StepOrder, targetStatus))
        {
            throw new InvalidProcessTransitionException(
                $"허용되지 않은 공정 전이입니다: 현재 {lot.CurrentStatus}(공정순서 {currentStep.StepOrder}) -> 요청 {targetStatus}(공정순서 {targetStep.StepOrder})");
        }

        // 2026-09-03 피드백(#2): 앞으로 이동(건너뛰기)할 때, 현재와 대상 "사이"에 입고검사/출고검사가 끼어 있으면
        // 그 공정은 건너뛰는 것이므로 차단한다(대상 자신이 검사공정인 경우는 건너뛰는 게 아니라 허용).
        if (targetStep.StepOrder > currentStep.StepOrder)
        {
            var skippedStepIds = steps
                .Where(s => s.StepOrder > currentStep.StepOrder && s.StepOrder < targetStep.StepOrder)
                .Select(s => s.ProcessDefinitionId)
                .ToList();

            if (skippedStepIds.Count > 0)
            {
                var skippedDefs = await _processDefinitions.ListAsync(
                    p => skippedStepIds.Contains(p.Id), cancellationToken);
                var blocked = skippedDefs.FirstOrDefault(p => NonSkippableOperCodes.Contains(p.OperCode));
                if (blocked is not null)
                {
                    throw new InvalidProcessTransitionException(
                        $"{blocked.ProcessName}({blocked.OperCode})은(는) 건너뛸 수 없습니다. 해당 공정을 먼저 완료해야 합니다.");
                }
            }
        }
    }
}
