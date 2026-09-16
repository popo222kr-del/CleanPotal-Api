using ClosedXML.Excel;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Web.Services;

public sealed class LotListExcelExporter
{
    public byte[] Export(IReadOnlyList<LotListItemDto> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("입출고 현황");
        var headers = new[]
        {
            "LINE", "업체명", "반출번호", "세정코드", "제품명", "S/N", "품목코드", "BATCH",
            "STATUS", "RECIPE", "설비명", "AETS 입고", "공정 입고", "TAT(시간)", "PROCESS", "작업자", "Comment"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var row = index + 2;
            object?[] values =
            {
                item.Line, item.PmEquipmentName, item.ExportNumber, item.CleaningCode, item.ProductName,
                item.SerialNumber, item.ItemCode, item.IsBatch ? "Y" : "", item.CurrentStatus.ToString(),
                item.RecipeCode, item.EquipmentId, item.ReceivedDate, item.StageArrivedAt, item.TatHours,
                item.ProcessLabel, item.Worker, item.Comment
            };
            for (var column = 0; column < values.Length; column++)
            {
                sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(8, 40);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

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
