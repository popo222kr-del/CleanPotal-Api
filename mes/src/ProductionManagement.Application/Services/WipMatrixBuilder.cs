using ProductionManagement.Application.DTOs;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// "입 · 출고 현황 조회" 재공현황 매트릭스 집계·필터·드릴다운 규칙 (2026-09-14 WPF LotManagementViewModel에서 승격).
//
// 예전엔 이 규칙이 WPF ViewModel 안에만 있어 웹이 재사용하지 못했다 - 앱과 웹이 같은 숫자를 내도록 순수 함수로
// 옮기고 양쪽이 이것만 호출한다. 동작은 승격 전과 완전히 같다(WipMatrixBuilderTests로 고정).
//
// 집계 기준(2026-08-25~09-03 피드백 누적):
//  - 재공현황: 제품(세정코드, 없으면 품목코드)별 × 공정 단계별 LOT 수. 재작업(Rework)은 공정과 무관하게 REPAIR 칸에만.
//  - 고객출하 칸 = 출하완료(Completed) + 8100에 있는 재작업 아닌 LOT. 모든 LOT이 정확히 한 칸에만 잡혀
//    합계가 하단 목록과 일치한다. 천안·동탄창고는 도메인에 창고 개념이 없어 항상 0(열만 유지).
//  - 입고현황 = 조회 기간 내 AETS 입고(ReceivedDate), 출고현황 = 조회 기간 내 고객출하/완료(StageArrivedAt).
public static class WipMatrixBuilder
{
    // REPAIR(재작업) 칸 드릴다운을 뜻하는 특수 단계 값.
    public const int RepairStage = -999;

    // 재공현황 그룹의 하위 칸 제목. 순서는 행의 StageCells와 정확히 같다(화면은 이 순서대로 그린다).
    public static readonly IReadOnlyList<string> StageColumnTitles = new[]
    {
        "INPUT", "IN INSP", "CLEANING", "DRY", "LASER&CO2", "BAKE", "REPAIR",
        "FI INSP", "BOXING", "고객출하", "천안창고", "동탄창고", "합계"
    };

    public static bool IsWip(LotStatus status)
        => status is LotStatus.Waiting or LotStatus.InProgress or LotStatus.Hold or LotStatus.Rework;

    // 업체명/세정코드/S/N 중 하나라도 입력했으면 입고/출고 현황(업체별)을 추가로 보여준다(2026-08-26 피드백).
    public static bool HasInOutFilter(WipMatrixFilter filter)
        => !string.IsNullOrWhiteSpace(filter.CustomerName)
           || !string.IsNullOrWhiteSpace(filter.CleaningCode)
           || !string.IsNullOrWhiteSpace(filter.SerialNumber);

    // 공정명 → OperCode (재공 단계 버킷 판정용). 앱 기존 방식과 동일하게 공정명 대소문자 구분.
    public static Dictionary<string, int> CreateOperCodeMap(IEnumerable<ProcessDefinitionDto> processes)
    {
        var map = new Dictionary<string, int>();
        foreach (var process in processes)
        {
            map[process.ProcessName] = process.OperCode;
        }
        return map;
    }

    // 세정코드 → 품목 구분(MAT GROUP). 세정코드는 대소문자 무시.
    public static Dictionary<string, string?> CreateCategoryMap(IEnumerable<ProductDto> products)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            map[product.CleaningCode] = product.ItemCategory;
        }
        return map;
    }

    // 업체명 드롭다운 옵션 = 데이터에 존재하는 업체명(중복 제거, 정렬).
    public static IReadOnlyList<string> CustomerOptions(IEnumerable<LotListItemDto> items)
        => items.Select(i => i.CustomerName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

    // 업체명(일치)/세정코드(부분)/S/N(부분) 필터. 날짜는 여기서 거르지 않는다(목록은 서버 기간 조회, 매트릭스는 날짜 무관).
    public static IReadOnlyList<LotListItemDto> ApplyFilters(IEnumerable<LotListItemDto> items, WipMatrixFilter filter)
    {
        var result = items;
        if (!string.IsNullOrWhiteSpace(filter.CustomerName))
        {
            result = result.Where(i => string.Equals(i.CustomerName, filter.CustomerName, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.CleaningCode))
        {
            var key = filter.CleaningCode.Trim();
            result = result.Where(i => i.CleaningCode?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        if (!string.IsNullOrWhiteSpace(filter.SerialNumber))
        {
            var key = filter.SerialNumber.Trim();
            result = result.Where(i => i.SerialNumber.Contains(key, StringComparison.OrdinalIgnoreCase));
        }
        return result.ToList();
    }

    public static WipMatrixResult Build(
        IEnumerable<LotListItemDto> lots,
        WipMatrixFilter filter,
        IReadOnlyDictionary<string, int> operCodeMap,
        IReadOnlyDictionary<string, string?> categoryMap)
    {
        var all = lots.ToList();

        var customers = all
            .Select(i => i.CustomerName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // 제품(세정코드)별로 묶는다 - 세정코드가 없으면 품목코드로 대체.
        var groups = all
            .GroupBy(i => i.CleaningCode ?? i.ItemCode)
            .OrderBy(g => g.Key)
            .ToList();

        var rows = new List<WipMatrixRowDto>();
        var totalRow = BuildRow(0, null, string.Empty, "합계", string.Empty, all, customers, isTotal: true, filter, operCodeMap);
        rows.Add(totalRow);

        var rowNo = 0;
        foreach (var g in groups)
        {
            rowNo++;
            var rep = g.First();
            var category = rep.CleaningCode is not null && categoryMap.TryGetValue(rep.CleaningCode, out var cat) ? cat : null;
            rows.Add(BuildRow(rowNo, rep.CleaningCode, string.IsNullOrWhiteSpace(category) ? "-" : category!,
                rep.ItemCode, rep.ProductName, g.ToList(), customers, isTotal: false, filter, operCodeMap));
        }

        return new WipMatrixResult(customers, rows);
    }

    private static WipMatrixRowDto BuildRow(
        int rowNo, string? cleaningCode, string matGroup, string matId, string matDesc,
        List<LotListItemDto> lots, List<string> customers, bool isTotal,
        WipMatrixFilter filter, IReadOnlyDictionary<string, int> operCodeMap)
    {
        int StageCount(int operCode) => lots.Count(l =>
            IsWip(l.CurrentStatus) &&
            l.CurrentStatus != LotStatus.Rework &&
            OperCodeOf(l, operCodeMap) == operCode);

        var inboundByCustomer = customers
            .Select(c => new MatrixInOutCell(
                IsInbound: true, AllProducts: isTotal, CleaningCode: cleaningCode, CustomerName: c,
                Count: lots.Count(l => l.CustomerName == c && IsInboundInPeriod(l, filter)),
                Label: isTotal ? c : matDesc))
            .ToList();
        var outboundByCustomer = customers
            .Select(c => new MatrixInOutCell(
                IsInbound: false, AllProducts: isTotal, CleaningCode: cleaningCode, CustomerName: c,
                Count: lots.Count(l => l.CustomerName == c && IsOutboundInPeriod(l, filter)),
                Label: isTotal ? c : matDesc))
            .ToList();

        // 업체별 칸 뒤에 "합계" 칸을 붙인다.
        inboundByCustomer.Add(new MatrixInOutCell(true, isTotal, cleaningCode, null, inboundByCustomer.Sum(c => c.Count), isTotal ? "전체" : matDesc));
        outboundByCustomer.Add(new MatrixInOutCell(false, isTotal, cleaningCode, null, outboundByCustomer.Sum(c => c.Count), isTotal ? "전체" : matDesc));

        var label = isTotal ? "전체" : matDesc;
        MatrixDrillTarget Target(int? stage) => new(isTotal, cleaningCode, stage, label);

        var input = StageCount(2000);
        var inInsp = StageCount(2100);
        var cleaning = StageCount(3000);
        var dry = StageCount(4000);
        var laser = StageCount(4100);
        var bake = StageCount(5000);
        var repair = lots.Count(l => l.CurrentStatus == LotStatus.Rework);
        var fiInsp = StageCount(7000);
        var boxing = StageCount(7100);
        var customerShip = lots.Count(l =>
            l.CurrentStatus == LotStatus.Completed
            || (l.CurrentStatus != LotStatus.Rework && OperCodeOf(l, operCodeMap) == 8100));
        const int warehouseCheonan = 0;
        const int warehouseDongtan = 0;
        var wipTotal = input + inInsp + cleaning + dry + laser + bake + repair + fiInsp + boxing
                       + customerShip + warehouseCheonan + warehouseDongtan;

        var stageCells = new List<MatrixStageCell>
        {
            new(input, Target(2000), null, false, false),
            new(inInsp, Target(2100), null, false, false),
            new(cleaning, Target(3000), null, false, false),
            new(dry, Target(4000), null, false, false),
            new(laser, Target(4100), null, false, false),
            new(bake, Target(5000), null, false, false),
            new(repair, Target(RepairStage), null, true, false),
            new(fiInsp, Target(7000), null, false, false),
            new(boxing, Target(7100), null, false, false),
            new(customerShip, Target(8100), "이 제품의 출하대기·출하완료 LOT 보기", false, false),
            new(warehouseCheonan, null, null, false, false),
            new(warehouseDongtan, null, null, false, false),
            new(wipTotal, Target(null), "이 제품의 재공 전체 보기", false, true)
        };

        return new WipMatrixRowDto(
            IsTotal: isTotal,
            RowNo: rowNo,
            CleaningCode: cleaningCode,
            MatGroup: isTotal ? string.Empty : matGroup,
            MatId: matId,
            MatDesc: matDesc,
            RowTarget: Target(null),
            StageCells: stageCells,
            InboundCells: inboundByCustomer,
            OutboundCells: outboundByCustomer);
    }

    // 재공현황 칸 클릭 → 해당 제품/공정단계 LOT.
    public static IReadOnlyList<LotListItemDto> DrillDown(
        IEnumerable<LotListItemDto> matrixSource, MatrixDrillTarget target, IReadOnlyDictionary<string, int> operCodeMap)
    {
        IEnumerable<LotListItemDto> lots = matrixSource;

        if (!target.AllProducts && target.CleaningCode is not null)
        {
            lots = lots.Where(l => string.Equals(l.CleaningCode, target.CleaningCode, StringComparison.OrdinalIgnoreCase));
        }

        if (target.StageOperCode is int stage)
        {
            lots = stage == RepairStage
                ? lots.Where(l => l.CurrentStatus == LotStatus.Rework)
                : lots.Where(l => IsWip(l.CurrentStatus) && l.CurrentStatus != LotStatus.Rework && OperCodeOf(l, operCodeMap) == stage);
        }

        return lots.ToList();
    }

    public static string DrillDownMessage(MatrixDrillTarget target, int count)
    {
        var scope = target.AllProducts ? "전체 제품" : target.Label;
        return $"[{scope}] 조건으로 {count}건 표시 중 (초기화하면 전체 목록)";
    }

    // 입고/출고 현황 칸 클릭 → 해당 제품 × 업체 × 입/출고 조건 LOT.
    public static IReadOnlyList<LotListItemDto> DrillInOut(
        IEnumerable<LotListItemDto> matrixSource, MatrixInOutCell cell, WipMatrixFilter filter)
    {
        IEnumerable<LotListItemDto> lots = matrixSource;

        if (!cell.AllProducts && cell.CleaningCode is not null)
        {
            lots = lots.Where(l => string.Equals(l.CleaningCode, cell.CleaningCode, StringComparison.OrdinalIgnoreCase));
        }
        if (cell.CustomerName is not null)
        {
            lots = lots.Where(l => string.Equals(l.CustomerName, cell.CustomerName, StringComparison.OrdinalIgnoreCase));
        }
        lots = cell.IsInbound
            ? lots.Where(l => IsInboundInPeriod(l, filter))
            : lots.Where(l => IsOutboundInPeriod(l, filter));

        return lots.ToList();
    }

    public static string DrillInOutMessage(MatrixInOutCell cell, int count)
    {
        var kind = cell.IsInbound ? "입고" : "출고";
        var scope = cell.AllProducts ? cell.CustomerName ?? "전체" : cell.Label;
        return $"[{scope} · {kind}] 조건으로 {count}건 표시 중 (초기화하면 전체 목록)";
    }

    private static bool IsInboundInPeriod(LotListItemDto l, WipMatrixFilter filter) => InPeriod(l.ReceivedDate, filter);

    private static bool IsOutboundInPeriod(LotListItemDto l, WipMatrixFilter filter)
        => (l.CurrentStatus == LotStatus.Completed || l.CurrentProcessName == "고객출하") && InPeriod(l.StageArrivedAt, filter);

    private static int OperCodeOf(LotListItemDto lot, IReadOnlyDictionary<string, int> operCodeMap)
        => lot.CurrentProcessName is not null && operCodeMap.TryGetValue(lot.CurrentProcessName, out var code) ? code : -1;

    private static bool InPeriod(DateTime date, WipMatrixFilter filter)
    {
        if (filter.DateFrom is { } from && date.Date < from.Date) { return false; }
        if (filter.DateTo is { } to && date.Date > to.Date) { return false; }
        return true;
    }
}
