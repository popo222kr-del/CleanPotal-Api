using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.BusinessRules;

// OPER 화면 "실행" 직전 게이트와 공정 구분 규칙 (2026-09-14 WPF OperViewModel에서 승격).
//
// 예전엔 WPF OperViewModel 안에만 있어 웹이 복제해야 했다 - 앱/웹이 같은 규칙을 쓰도록 순수 규칙으로 옮겼다.
// 동작은 승격 전과 동일(OperExecutionRulesTests로 고정). 실행 게이트 순서(화면이 이 순서대로 호출):
//   사유코드 → 레시피/설비 → SPEC OUT(확인) → 출고검사 NG(확인) → READ TIME → 패널 저장 → TRAN
//   (READ TIME 은 3000·4000, 출고검사 NG 는 7000 이라 둘이 같이 걸리는 일은 없다. 순서를 적어 두는 것은
//    화면을 다시 만들 때 게이트를 빠뜨리지 않기 위해서다.)
//   (2100/7000 완료·출하는 출력 관리 창을 닫을 때 TRAN 이동)
public static class OperExecutionRules
{
    // 레시피를 실제로 사용하는 OPER(세정/건조/Laser&CO2/Bake - TranCode.Start가 있는 4개 공정)만
    // RECIPE ID/RES ID 입력을 활성화한다(2026-08-24 피드백).
    public static bool IsRecipeOper(int operCode) => operCode is 3000 or 4000 or 4100 or 5000;

    // 세정/건조는 레시피·설비를 반드시 채워야 실행할 수 있다(2026-08-18 피드백).
    public static bool RequiresRecipeAndEquipment(int operCode) => operCode is 3000 or 4000;

    // 입고(2000)/포장완료(7100)/고객출하(8100)는 다중 선택 일괄 TRAN 가능(2026-08-31 피드백 #13).
    public static bool SupportsMultiSelect(int operCode) => operCode is 2000 or 7100 or 8100;

    // 검사값을 입력하는 공정은 입고검사(2100)와 출고검사(7000) 둘뿐이다.
    public static bool IsInspectionOper(int operCode) => operCode is 2100 or 7000;

    // HOLD/RELEASE/재작업/SKIP/SHIP은 사유 코드가 필요하다. 필요 없으면 null.
    public static ReasonCategory? ReasonCategoryFor(TranCode? tranCode) => tranCode switch
    {
        TranCode.Hold => ReasonCategory.Hold,
        TranCode.Release => ReasonCategory.Release,
        TranCode.Rework => ReasonCategory.Rework,
        TranCode.Skip => ReasonCategory.Skip,
        TranCode.Ship => ReasonCategory.Ship,
        _ => null
    };

    public static bool RequiresReasonCode(TranCode? tranCode) => ReasonCategoryFor(tranCode) is not null;

    public static bool IsMissingRecipeOrEquipment(int operCode, bool hasRecipe, string? resId)
        => RequiresRecipeAndEquipment(operCode) && (!hasRecipe || string.IsNullOrWhiteSpace(resId));

    // 세정/건조는 "완료(End)"로 다음 공정에 넘길 때만 READ TIME을 검사한다(Start/Hold/Release 등 같은 OPER 내
    // 전이는 검사 안 함). 경과는 작업 시작 시각부터. CMT_AETS 코멘트가 있으면 통과(2026-08-26 피드백).
    // 통과면 null, 차단이면 안내 문구.
    public static string? GetReadTimeBlockMessage(
        int operCode, TranCode tranCode, int? readTimeMinutes, string? cmtAets, DateTime? startedAt, DateTime now)
    {
        if (!RequiresRecipeAndEquipment(operCode) || tranCode != TranCode.End) { return null; }
        if (readTimeMinutes is not { } readMinutes || readMinutes <= 0) { return null; }
        if (!string.IsNullOrWhiteSpace(cmtAets)) { return null; }

        var elapsedMinutes = startedAt is { } s ? (now - s).TotalMinutes : 0;
        if (elapsedMinutes >= readMinutes) { return null; }

        return $"세정, 건조가 완료되지 않았습니다.\n(RECIPE READ TIME {readMinutes}분 중 {Math.Floor(elapsedMinutes)}분 경과. 특이사항이 있으면 CMT_AETS 코멘트를 입력하면 넘길 수 있습니다.)";
    }

    // 출고검사(7000)를 완료(End)로 넘길 때 합부판정이 부적합(NG)이면 확인 창이 필요하다(2026-08-28 피드백).
    public static bool RequiresOutgoingNgConfirmation(int operCode, TranCode tranCode, string? resultValue)
        => operCode == 7000 && tranCode == TranCode.End
           && string.Equals(resultValue?.Trim(), "NG", StringComparison.OrdinalIgnoreCase);

    // 입고검사(2100)/출고검사(7000)를 완료(End)/출하(Ship)로 넘길 때는 출력 관리 창을 먼저 띄우고, 그 창을
    // 닫는 시점에 전산이 이동한다(2026-09-01 피드백 #9).
    public static bool DefersToOutputManagement(int operCode, TranCode tranCode)
        => operCode is 2100 or 7000 && tranCode is TranCode.End or TranCode.Ship;
}
