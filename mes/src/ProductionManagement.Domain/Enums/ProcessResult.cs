namespace ProductionManagement.Domain.Enums;

// 검사 판정. 입고검사/출고검사에서 기록하며, 세정 이력 조회의 "합/부" 열은 출고검사(OPER 7000)
// 시점의 이 값을 그대로 따라간다(합=Pass, 부=Fail).
public enum ProcessResult
{
    Pass = 0,
    Fail = 1
}
