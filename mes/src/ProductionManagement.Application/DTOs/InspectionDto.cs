namespace ProductionManagement.Application.DTOs;

// PARAMETER ID / DESC를 각각 내려주고 표시는 UI가 담당한다(2026-08-19 피드백: IN INSP/FI INSP 헤더를
// "PARAMETER ID / DESC / MIN / MAX / IN VALUE / (FI VALUE) / COMMENT"로 재구성). InputValue는 이제
// 자유 텍스트 측정값이 아니라 "특이사항 유/무"를 Y/N으로 고르는 값이다. ReferenceInputValue는 FI INSP
// 행에서만 채워지며, 같은 파라미터의 IN INSP(2100) 값을 그대로 참고용으로 보여준다("IN VALUE - 입고검사
// 데이터를 끌고옴").
public record InspectionParameterRowDto(
    int ParameterDefinitionId,
    string Code,
    string Description,
    decimal? MinValue,
    decimal? MaxValue,
    string? InputValue,
    string? Comment,
    string? ReferenceInputValue = null,
    // 2026-08-26: 측정형(Numeric)은 숫자 입력, 그 외(외관 Boolean/Choice)는 Y/N 드롭다운으로 입력하도록
    // UI가 구분할 수 있게 파라미터 타입을 함께 내려준다.
    Domain.Enums.ParameterType ParameterType = Domain.Enums.ParameterType.Boolean,
    // 2026-09-01 피드백(#8): 측정 포인트 수(VALUE COUNT). 2 이상이면 계측값 입력 칸을 A/B/C/D…로 나눈다.
    // 저장 시에는 각 포인트 값을 '|'로 이어 InputValue 하나에 담는다.
    int ValueCount = 1);

// MatId=세정코드, PmResId=전산등록 시 PM설비명(고정), ResId=이 시도에서 실제 진행한 설비호기(수기입력),
// CmtAets=작업자 코멘트. ClnCount=S/N 기준 입고 횟수 - 같은 물리적 부품(재사용 쿼츠 보트 등)이 S/N을
// 유지한 채 여러 번 전산등록될 수 있어, 이 S/N을 가진 Lot이 전체 시스템에 몇 건 있는지로 센다
// (현재 공정 시도 횟수 AttemptNumber가 아님 - 2026-08-18 사용자 확정).
//
// IN INSP는 2100(입고검사) 자신과 7000(출고검사)에서 함께 보인다(7000에서는 참고용 읽기전용) - FI INSP는
// 7000에서만 보인다. InInspEditable이 false면 그 화면에서는 IN INSP 값을 수정할 수 없다(이미 2100에서
// 확정된 값이므로).
public record InspectionPanelDto(
    int LotId,
    string LotNumber,
    string MatId,
    string MatDesc,
    string Sn,
    int ClnCount,
    int? RecipeDefinitionId,
    string? RecipeId,
    string? PmResId,
    string? ResId,
    string? CmtAets,
    bool ShowsInInsp,
    bool InInspEditable,
    bool ShowsFiInsp,
    IReadOnlyList<InspectionParameterRowDto> InInspRows,
    IReadOnlyList<InspectionParameterRowDto> FiInspRows,
    // 2026-08-28 피드백: 공정 이동 시 이전 코멘트가 사라져 보이지 않는 문제 → 이 LOT의 모든 공정 코멘트를
    // 시간순으로 누적해 읽기전용으로 보여준다(현재 OPER의 편집 가능한 CmtAets와 별개).
    string? CommentHistory = null);

public record InspectionParameterInputDto(int ParameterDefinitionId, string? InputValue, string? Comment);

// ParameterInputs는 항상 "현재 OPER에서 편집 가능한 표"(2100이면 IN INSP, 7000이면 FI INSP)에만 적용된다.
// RecipeDefinitionId는 세정/건조 등 레시피가 있는 공정에서 드롭다운으로 고른 값 - 세정코드(제품)의
// 공정별 레시피 배정(ProductRecipeAssignment)을 그대로 갱신한다.
public record SaveInspectionPanelRequest(int LotId, int? RecipeDefinitionId, string? ResId, string? CmtAets, IReadOnlyList<InspectionParameterInputDto> ParameterInputs);
