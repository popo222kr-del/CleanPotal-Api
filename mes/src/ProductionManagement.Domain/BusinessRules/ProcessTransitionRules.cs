using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.BusinessRules;

// Process State Machine의 핵심 규칙. UI가 아니라 여기(및 이를 호출하는 Application Service)에서 검증한다
// (CLAUDE.md 8번: State Transition 검증은 UI 검증만으로 끝내지 않음).
//
// 공정 순서는 하드코딩하지 않고 ProcessRouteStep.StepOrder로 표현되므로, 이 규칙은 구체적인 공정 이름이 아니라
// "현재 StepOrder/상태"와 "목표 StepOrder/상태"만 가지고 판단한다. Rollback(뒤로 가기)은 여기서 허용하지
// 않는다 - 관리자 전용 별도 경로로만 가능하다 (Phase 9).
public static class ProcessTransitionRules
{
    public static bool IsAllowed(int currentStepOrder, LotStatus currentStatus, int targetStepOrder, LotStatus targetStatus)
    {
        if (currentStepOrder == targetStepOrder)
        {
            return (currentStatus, targetStatus) switch
            {
                (LotStatus.Waiting, LotStatus.InProgress) => true,
                (LotStatus.InProgress, LotStatus.Completed) => true,
                (LotStatus.InProgress, LotStatus.Hold) => true,
                (LotStatus.InProgress, LotStatus.Rework) => true,
                (LotStatus.Hold, LotStatus.InProgress) => true,
                (LotStatus.Hold, LotStatus.Rework) => true,
                (LotStatus.Rework, LotStatus.InProgress) => true,
                (LotStatus.Rework, LotStatus.Completed) => true,
                (LotStatus.Rework, LotStatus.Hold) => true,
                _ => false
            };
        }

        // Phase 14: 공정 완료 시 다음 공정을 드롭다운에서 직접 선택하는 방식으로 바뀌면서, "바로 다음
        // 한 단계"뿐 아니라 같은 Route 안에서 더 뒤에 있는 임의의 단계로 건너뛰는 것도 허용한다
        // (예: 이 Lot에는 필요 없는 중간 단계를 의도적으로 건너뜀). 단, 이전 공정이 반드시 완료된
        // 상태여야 하고, 뒤로 가는 것(Rollback)은 여전히 이 규칙으로 허용하지 않는다.
        if (targetStepOrder > currentStepOrder)
        {
            return currentStatus == LotStatus.Completed
                && (targetStatus == LotStatus.Waiting || targetStatus == LotStatus.InProgress);
        }

        return false;
    }
}
