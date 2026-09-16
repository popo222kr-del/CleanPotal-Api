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
