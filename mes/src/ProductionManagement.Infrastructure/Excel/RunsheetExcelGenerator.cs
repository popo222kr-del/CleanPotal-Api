using ClosedXML.Excel;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Infrastructure.Excel;

// 런시트(공정 진행표) xlsx 생성. 포털(React 화면)과 MES 화면이 같이 쓰므로 공용 계층에 둔다
// — 화면 쪽에 두면 포털이 베껴 가야 하고, 그 순간 두 런시트의 서식이 갈라진다.
public sealed class RunsheetExcelGenerator : IRunsheetGenerator
{
    public string Generate(RunsheetData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("런시트");

        sheet.Cell(1, 1).Value = "런시트 (공정 진행표)";
        sheet.Range(1, 1, 1, 8).Merge();
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 15;
        sheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        WriteInfo(sheet, 3, "LOT ID", data.LotNumber, "S/N", data.SerialNumber);
        WriteInfo(sheet, 4, "세정코드", data.MatId, "품목명", data.MatDesc);
        WriteInfo(sheet, 5, "업체", data.CustomerName, "LINE", data.Line);
        WriteInfo(sheet, 6, "생성일시", data.GeneratedAt.ToString("yyyy-MM-dd HH:mm"), "", "");

        var headers = new[] { "NO", "OPER", "공정", "TRAN", "시각", "설비(RES)", "작업자", "비고" };
        for (var column = 0; column < headers.Length; column++)
        {
            var cell = sheet.Cell(8, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        for (var index = 0; index < data.Rows.Count; index++)
        {
            var item = data.Rows[index];
            var row = index + 9;
            sheet.Cell(row, 1).Value = index + 1;
            sheet.Cell(row, 2).Value = item.OperCode;
            sheet.Cell(row, 3).Value = item.OperDesc;
            sheet.Cell(row, 4).Value = item.TranCode;
            sheet.Cell(row, 5).Value = item.TranTime;
            sheet.Cell(row, 6).Value = item.ResId ?? "-";
            sheet.Cell(row, 7).Value = item.UserDesc;
            sheet.Cell(row, 8).Value = item.Comment ?? string.Empty;
        }

        sheet.Columns().AdjustToContents(8, 40);
        var safeLot = string.Concat(data.LotNumber.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(Path.GetTempPath(), $"{safeLot}_runsheet_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        workbook.SaveAs(path);
        return path;
    }

    private static void WriteInfo(IXLWorksheet sheet, int row, string label1, string? value1, string label2, string? value2)
    {
        sheet.Cell(row, 1).Value = label1;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 2).Value = value1 ?? "-";
        sheet.Range(row, 2, row, 4).Merge();
        sheet.Cell(row, 5).Value = label2;
        sheet.Cell(row, 5).Style.Font.Bold = true;
        sheet.Cell(row, 6).Value = value2 ?? "-";
        sheet.Range(row, 6, row, 8).Merge();
    }
}
