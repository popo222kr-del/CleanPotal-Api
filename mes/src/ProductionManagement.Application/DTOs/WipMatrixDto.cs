namespace ProductionManagement.Application.DTOs;

// "입 · 출고 현황 조회" 재공현황 매트릭스 자료 (2026-09-14 WPF ViewModel에서 승격 - 앱·웹 공용).
// 집계 규칙은 Application/Services/WipMatrixBuilder.cs 한 곳에만 있다.

/// <summary>
/// 재공현황 매트릭스 셀 클릭(드릴다운) 대상. 어떤 제품(CleaningCode)의 어떤 공정단계(StageOperCode)를
/// 눌렀는지 담아 하단 목록을 그 조건으로 필터링한다. AllProducts=true면(Total 행) 제품 무관 해당 단계
/// 전체. StageOperCode=null이면(MAT DESC/MAT ID 클릭) 단계 무관 해당 제품 전체.
/// StageOperCode = -999(WipMatrixBuilder.RepairStage)는 REPAIR(재작업 상태)를 뜻하는 특수 값.
/// </summary>
public sealed record MatrixDrillTarget(bool AllProducts, string? CleaningCode, int? StageOperCode, string Label);

/// <summary>
/// 입고현황/출고현황의 업체별 셀 하나. 셀 클릭 시 이 조건(제품 × 업체 × 입/출고)으로 하단 목록을 필터링한다.
/// AllProducts=true(Total 행)면 제품 무관 해당 업체 전체. CustomerName=null(합계 셀)이면 업체 무관 전체.
/// IsInbound=true면 입고(조회 기간 내 AETS 입고), false면 출고(기간 내 고객출하/완료).
/// </summary>
public sealed record MatrixInOutCell(bool IsInbound, bool AllProducts, string? CleaningCode, string? CustomerName, int Count, string Label);

/// <summary>
/// 재공현황(공정 단계별 WIP) 칸 하나. 화면은 셀 템플릿 하나로 모든 칸을 그리고, 어떤 칸인지는 이 객체가 든다.
/// </summary>
/// <param name="Count">표시 수량(0이면 화면엔 빈칸).</param>
/// <param name="Target">클릭 시 드릴다운 대상. null이면 클릭할 수 없는 칸(천안·동탄창고).</param>
/// <param name="ToolTip">칸에 붙일 설명(없으면 null).</param>
/// <param name="Emphasize">재작업(REPAIR)처럼 경고색으로 강조할 칸.</param>
/// <param name="IsSummary">합계 칸(배경 강조).</param>
public sealed record MatrixStageCell(int Count, MatrixDrillTarget? Target, string? ToolTip, bool Emphasize, bool IsSummary);

/// <summary>
/// 매트릭스 한 행. IsTotal 행은 맨 위 합계 행(RowNo=0). MatGroup = 품목 구분(없으면 "-", 합계 행은 빈칸),
/// MatId = 품목코드(합계 행은 "합계"), MatDesc = 제품명.
/// StageCells 순서는 WipMatrixBuilder.StageColumnTitles와 정확히 같다.
/// InboundCells/OutboundCells는 업체별 칸 뒤에 합계 칸이 하나 붙어 있다(업체 순서 = WipMatrixResult.Customers).
/// </summary>
public sealed record WipMatrixRowDto(
    bool IsTotal,
    int RowNo,
    string? CleaningCode,
    string MatGroup,
    string MatId,
    string MatDesc,
    MatrixDrillTarget RowTarget,
    IReadOnlyList<MatrixStageCell> StageCells,
    IReadOnlyList<MatrixInOutCell> InboundCells,
    IReadOnlyList<MatrixInOutCell> OutboundCells);

/// <summary>
/// 화면 필터. CustomerName = 화면 "업체명" 드롭다운(LOT의 CustomerName과 대소문자 무시 일치),
/// CleaningCode/SerialNumber = 부분 일치, DateFrom/DateTo = 입고/출고 현황의 기간(날짜 단위, 경계 포함).
/// </summary>
public sealed record WipMatrixFilter(string? CustomerName, string? CleaningCode, string? SerialNumber, DateTime? DateFrom, DateTime? DateTo);

/// <summary>매트릭스 결과: 입고/출고 업체 열 목록 + 행(맨 앞이 합계 행).</summary>
public sealed record WipMatrixResult(IReadOnlyList<string> Customers, IReadOnlyList<WipMatrixRowDto> Rows);
