namespace ProductionManagement.Application.DTOs;

// 앱이 생성하는 "런시트(공정 진행표)"의 데이터 한 세트. 이 LOT(정확히는 같은 S/N 사이클)의 공정 전이
// 이력을 요약해 Excel로 출력한다(2026-08-27 피드백: RUNSHEET = 앱이 공정 이력으로 생성).
public record RunsheetData(
    string LotNumber,
    string SerialNumber,
    string MatId,       // 세정코드
    string MatDesc,     // 품목명
    string CustomerName,
    string? Line,
    DateTime GeneratedAt,
    IReadOnlyList<RunsheetRow> Rows);

public record RunsheetRow(
    int OperCode,
    string OperDesc,
    string TranCode,
    DateTime TranTime,
    string? ResId,
    string? Comment,
    string UserDesc);
