using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Infrastructure.Data;

// 세정 이력·공정 이력 조회 전용 저장소.
// 이 화면의 어려운 점은 "표시할 열이 제품마다 다르다"는 것이다. 검사 파라미터가 제품별로 배정되므로,
// 조회 결과에 실제로 등장한 파라미터를 모아 그때그때 열을 만들어 낸다(2단 그룹 헤더의 재료).
// 식각량(입고검사 무게 - 출고검사 무게)처럼 저장돼 있지 않고 계산으로 나오는 열도 여기서 만든다.
public class EfProcessHistoryQueryRepository : IProcessHistoryQueryRepository
{
    private readonly ApplicationDbContext _context;

    public EfProcessHistoryQueryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProcessHistoryItemFullDto>> SearchAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.ProcessHistories.AsNoTracking().AsQueryable();

        if (request.CustomerId is int customerId)
        {
            query = query.Where(h => h.Lot.Product.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(request.CleaningCode))
        {
            var cleaningCode = request.CleaningCode.Trim();
            query = query.Where(h => h.Lot.Product.CleaningCode.Contains(cleaningCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var itemCode = request.ItemCode.Trim();
            query = query.Where(h => h.Lot.Product.ItemCode.Contains(itemCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ExportNumber))
        {
            var exportNumber = request.ExportNumber.Trim();
            query = query.Where(h => h.Lot.Registration != null && h.Lot.Registration.ExportNumber.Contains(exportNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            var productName = request.ProductName.Trim();
            query = query.Where(h => h.Lot.Product.ProductName.Contains(productName));
        }

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var serialNumber = request.SerialNumber.Trim();
            query = query.Where(h => h.Lot.SerialNumber.Contains(serialNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProcessName))
        {
            var processName = request.ProcessName.Trim();
            query = query.Where(h => h.ProcessDefinition.ProcessName.Contains(processName));
        }

        if (request.Result is { } result)
        {
            query = query.Where(h => h.Result == result);
        }

        if (!string.IsNullOrWhiteSpace(request.LotNumber))
        {
            var lotNumber = request.LotNumber.Trim();
            query = query.Where(h => h.Lot.LotNumber.Contains(lotNumber));
        }

        return await query
            .OrderByDescending(h => h.StartedAt)
            .Take(request.Take)
            .Select(h => new ProcessHistoryItemFullDto(
                h.Id,
                h.LotId,
                h.Lot.LotNumber,
                h.Lot.Product.ProductName,
                h.Lot.Product.Customer.CustomerName,
                h.ProcessDefinition.ProcessName,
                h.Worker,
                h.StartedAt,
                h.CompletedAt,
                h.Status,
                h.Result,
                h.Quantity,
                h.DefectQuantity,
                h.AttemptNumber,
                h.Remarks,
                h.EquipmentId,
                h.IsVoided))
            .ToListAsync(cancellationToken);
    }

    // 2026-09-03: 세정 이력 조회를 LOT 단위 "현재 상태 한 줄"로 보여주기 위한 조회. 위 SearchAsync가
    // 공정별 이력 여러 줄을 돌려주는 것과 달리, 조건에 맞는 각 LOT을 1행(현재 공정/현재 상태)으로 돌려준다.
    // 입고일(ReceivedDate) 기간 필터를 지원하고, 결과(Result) 필터는 그 LOT의 가장 최근 비무효 이력의
    // 판정을 기준으로 건다. 출하 완료 LOT도 상태와 무관하게 계속 조회된다(작업 목록에서만 빠진다).
    // 2026-09-08: SearchLotCurrentAsync와 SearchLotHistoryAsync가 완전히 같은 검색 조건을 쓰므로
    // 조건부 필터를 여기 하나로 모았다(둘 중 하나만 고치는 실수를 막는다).
    private IQueryable<Lot> ApplyLotFilters(ProcessHistorySearchRequest request)
    {
        var query = _context.Lots.AsNoTracking().AsQueryable();

        if (request.CustomerId is int customerId)
        {
            query = query.Where(l => l.Product.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(request.CleaningCode))
        {
            var cleaningCode = request.CleaningCode.Trim();
            query = query.Where(l => l.Product.CleaningCode.Contains(cleaningCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var itemCode = request.ItemCode.Trim();
            query = query.Where(l => l.Product.ItemCode.Contains(itemCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ExportNumber))
        {
            var exportNumber = request.ExportNumber.Trim();
            query = query.Where(l => l.Registration != null && l.Registration.ExportNumber.Contains(exportNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            var productName = request.ProductName.Trim();
            query = query.Where(l => l.Product.ProductName.Contains(productName));
        }

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var serialNumber = request.SerialNumber.Trim();
            query = query.Where(l => l.SerialNumber.Contains(serialNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProcessName))
        {
            var processName = request.ProcessName.Trim();
            query = query.Where(l => l.CurrentProcessDefinition.ProcessName.Contains(processName));
        }

        if (request.Result is ProcessResult result)
        {
            query = query.Where(l => _context.ProcessHistories
                .Where(h => h.LotId == l.Id && !h.IsVoided)
                .OrderByDescending(h => h.StartedAt)
                .Select(h => h.Result)
                .FirstOrDefault() == result);
        }

        if (request.DateFrom is { } dateFrom)
        {
            query = query.Where(l => l.ReceivedDate >= dateFrom.Date);
        }

        if (request.DateTo is { } dateTo)
        {
            var exclusiveEnd = dateTo.Date.AddDays(1);
            query = query.Where(l => l.ReceivedDate < exclusiveEnd);
        }

        return query;
    }

    // 입고검사(2100) / 출고검사(7000) - 세정 이력의 파라미터·검사 블록은 이 두 OPER의 기록만 본다.
    private const int InInspectionOper = 2100;
    private const int FiInspectionOper = 7000;

    // 파라미터 코드 → 표시 그룹. 사용자 지정 순서 그대로: Visual / Weight / Thickness / Roughness / Particle
    // (2026-09-08 지시 #5). 코드는 제품 마스터(ParameterDefinition.Code)에 실제로 들어있는 값들이며,
    // 여기 없는 코드는 임의로 묶지 않고 "기타" 그룹에 코드 그대로 내보낸다(마스터 데이터 우선 원칙 -
    // 새 코드가 생기면 화면에는 보이되 어느 그룹인지 여기서 정해줘야 한다는 걸 눈으로 알 수 있게).
    private static readonly (string Group, string[] Codes)[] ParameterGroups =
    {
        ("Visual", new[] { "CHIP", "CRACK", "BROKEN", "SCRATCH", "SCR", "PIT", "STAIN", "STRIP", "UNETCH", "HOLE", "VISUAL", "Etc" }),
        ("Weight", new[] { "WEIGHT" }),
        ("Thickness", new[] { "THK", "THICKNESS" }),
        ("Roughness", new[] { "ROUGH", "ROUGHNESS" }),
        ("Particle", new[] { "PARTICLE" })
    };

    private const string OtherParameterGroup = "기타";

    // 파라미터 블록의 기본 열 폭. 2026-09-09 피드백으로 62 → 74로 넓혔다(헤더 글자와 값이 빠듯했음).
    private const double DefaultLeafWidth = 74;

    // 무게 계열은 더 넓게 - "무게는 10,000g까지 재는 경우가 있다"(2026-09-09 지시). 식각량도 같은 폭.
    private const double WeightLeafWidth = 92;

    private const string WeightGroup = "Weight";

    // 식각량 = 입고검사 무게 - 출고검사 무게. 출고 Weight 그룹 안, 출고 무게 바로 오른쪽 칸이다(지시).
    private const string EtchAmountKey = "ETCH_AMOUNT";

    // 합부판정 파라미터. 앞쪽 "합/부" 컬럼이 같은 값(출고검사 판정)을 보여주므로 파라미터 열에서는 뺀다.
    private const string ResultParameterCode = "RESULT";

    private static int GroupIndexOf(string code)
    {
        for (var i = 0; i < ParameterGroups.Length; i++)
        {
            if (ParameterGroups[i].Codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return ParameterGroups.Length;
    }

    private static string GroupNameOf(string code)
    {
        var index = GroupIndexOf(code);
        return index < ParameterGroups.Length ? ParameterGroups[index].Group : OtherParameterGroup;
    }

    // 값이 여러 개인 파라미터(ValueCount >= 2)는 저장 시 "|"로 이어 붙인다 - A/B/C/D 열로 쪼개 보여준다.
    private static string?[] SplitValues(string? input, int count)
    {
        var parts = string.IsNullOrEmpty(input) ? Array.Empty<string>() : input.Split('|');
        var result = new string?[count];
        for (var i = 0; i < count; i++)
        {
            var value = i < parts.Length ? parts[i].Trim() : null;
            result[i] = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return result;
    }

    public async Task<LotHistoryResultDto> SearchLotHistoryAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default)
    {
        var rows = await ApplyLotFilters(request)
            .OrderByDescending(l => l.ReceivedDate)
            .ThenByDescending(l => l.Id)
            .Take(request.Take)
            .Select(l => new
            {
                l.Id,
                l.ProductId,
                l.LotNumber,
                ExportNumber = l.Registration != null ? l.Registration.ExportNumber : null,
                l.Product.CleaningCode,
                l.Product.ProductName,
                l.InitialSerialNumber,
                l.SerialNumber,
                Line = l.Registration != null ? l.Registration.Line : null,
                PmEquipmentName = l.Registration != null ? l.Registration.PmEquipmentName : null,
                TeamName = l.Registration != null ? l.Registration.TeamName : null,
                l.Product.ItemCategory,
                l.ReceivedDate,
                l.CurrentStatus,
                ShipDate = l.Registration != null ? (DateTime?)l.Registration.ShipDate : null
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new LotHistoryResultDto(Array.Empty<LotHistoryRowDto>(), Array.Empty<LotHistoryColumnGroupDto>());
        }

        var lotIds = rows.Select(r => r.Id).ToList();
        var serials = rows.Select(r => r.SerialNumber).Distinct().ToList();

        // 사용횟수: 같은 S/N으로 입고된 LOT 수(누적 입고 횟수).
        var usageCounts = await _context.Lots.AsNoTracking()
            .Where(l => serials.Contains(l.SerialNumber))
            .GroupBy(l => l.SerialNumber)
            .Select(g => new { SerialNumber = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var usageBySerial = usageCounts.ToDictionary(x => x.SerialNumber, x => x.Count);

        // ── 2026-09-09 2단계: 입고/출고 파라미터 + 검사 블록 ────────────────────────────────
        // 열은 "값이 있는 것"이 아니라 "이 제품들이 검사하기로 되어 있는 항목"(제품별 파라미터 마스터)에서
        // 만든다 - 값이 비어 있어도 열은 그대로 보여야 제품끼리 나란히 비교할 수 있다.
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();

        // RESULT(합부판정)는 앞쪽 "합/부" 컬럼과 같은 값이라 열에서 뺀다(2026-09-09 지시: 중복).
        var parameterDefs = await _context.ParameterDefinitions.AsNoTracking()
            .Where(p => p.ProductId != null && productIds.Contains(p.ProductId.Value) && p.IsActive
                        && p.Code != ResultParameterCode
                        && (p.Oper == "2100" || p.Oper == "7000"))
            .Select(p => new { p.Id, p.Code, p.Oper, p.SortOrder, p.ValueCount })
            .ToListAsync(cancellationToken);

        // 같은 코드라도 제품마다 별개 행이라 (OPER, 코드)로 합친다. 열 개수는 그 코드에 잡힌 최대 ValueCount
        // (제품마다 다르면 넓은 쪽에 맞춰야 값이 잘리지 않는다).
        var columnDefs = parameterDefs
            .GroupBy(p => new { Oper = p.Oper == "7000" ? FiInspectionOper : InInspectionOper, p.Code })
            .Select(g => new
            {
                g.Key.Oper,
                g.Key.Code,
                SortOrder = g.Min(x => x.SortOrder),
                ValueCount = Math.Max(1, g.Max(x => x.ValueCount))
            })
            .OrderBy(x => x.Oper == FiInspectionOper)                 // 입고 블록 먼저, 출고 블록 다음
            .ThenBy(x => GroupIndexOf(x.Code))                        // Visual / Weight / Thickness / Roughness / Particle
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToList();

        // 파라미터 측정값(InspectionRecord). (LOT, OPER, 코드) 하나당 값 하나다.
        var inspectionValues = await _context.InspectionRecords.AsNoTracking()
            .Where(r => lotIds.Contains(r.LotId)
                        && (r.ProcessDefinition.OperCode == InInspectionOper || r.ProcessDefinition.OperCode == FiInspectionOper))
            .Select(r => new { r.LotId, Oper = r.ProcessDefinition.OperCode, r.ParameterDefinition.Code, r.InputValue })
            .ToListAsync(cancellationToken);

        var valueByLot = inspectionValues
            .GroupBy(v => (v.LotId, v.Oper, v.Code))
            .ToDictionary(g => g.Key, g => g.Select(x => x.InputValue).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));

        // 검사 블록(DATE / TIME / 작업자)과 합/부: 그 OPER의 가장 최근 비무효 공정 이력 기준.
        var inspectionRuns = await _context.ProcessHistories.AsNoTracking()
            .Where(h => lotIds.Contains(h.LotId) && !h.IsVoided
                        && (h.ProcessDefinition.OperCode == InInspectionOper || h.ProcessDefinition.OperCode == FiInspectionOper))
            .Select(h => new { h.LotId, Oper = h.ProcessDefinition.OperCode, h.Worker, h.StartedAt, h.CompletedAt, h.Result })
            .ToListAsync(cancellationToken);

        var runByLot = inspectionRuns
            .GroupBy(h => (h.LotId, h.Oper))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).First());

        var columnGroups = BuildColumnGroups(columnDefs.Select(c => (c.Oper, c.Code, c.ValueCount)));

        // 전산등록에서 비워둔 항목은 빈 문자열로 저장돼 화면에 아무것도 안 보인다(TargetNullValue가 안 걸림).
        // 표시 일관성을 위해 빈 값은 null로 내려 그리드에서 "-"로 보이게 한다.
        static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        return new LotHistoryResultDto(
            rows.Select(r =>
            {
                // 결과 전체에서 쓰이는 열 키를 전부(값이 없어도 null로) 채워 둔다 - 화면이 Values[키]로
                // 읽을 때 키가 없어 바인딩이 깨지는 일이 없도록.
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);

                foreach (var column in columnDefs)
                {
                    var raw = valueByLot.GetValueOrDefault((r.Id, column.Oper, column.Code));
                    var parts = SplitValues(raw, column.ValueCount);
                    for (var i = 0; i < column.ValueCount; i++)
                    {
                        values[ValueKey(column.Oper, column.Code, i)] = parts[i];
                    }
                }

                // 식각량 = 입고검사 무게 - 출고검사 무게. 두 값이 다 숫자로 읽힐 때만 계산하고, 하나라도
                // 없거나 숫자가 아니면 비워 둔다("-"). 무게가 여러 값으로 기록되는 제품이면 첫 값 기준.
                if (double.TryParse(values.GetValueOrDefault(ValueKey(InInspectionOper, "WEIGHT", 0)), NumberStyles.Any, CultureInfo.InvariantCulture, out var inWeight)
                    && double.TryParse(values.GetValueOrDefault(ValueKey(FiInspectionOper, "WEIGHT", 0)), NumberStyles.Any, CultureInfo.InvariantCulture, out var fiWeight))
                {
                    values[EtchAmountKey] = (inWeight - fiWeight).ToString("0.##", CultureInfo.InvariantCulture);
                }
                else
                {
                    values[EtchAmountKey] = null;
                }

                foreach (var oper in new[] { InInspectionOper, FiInspectionOper })
                {
                    var run = runByLot.GetValueOrDefault((r.Id, oper));
                    var at = run?.CompletedAt ?? run?.StartedAt;
                    values[InspectionKey(oper, "DATE")] = at?.ToString("yyyy-MM-dd");
                    values[InspectionKey(oper, "TIME")] = at?.ToString("HH:mm");
                    values[InspectionKey(oper, "WORKER")] = NullIfBlank(run?.Worker);
                }

                return new LotHistoryRowDto(
                    r.Id,
                    r.LotNumber,
                    NullIfBlank(r.ExportNumber),
                    r.CleaningCode,
                    r.ProductName,
                    NullIfBlank(r.InitialSerialNumber),
                    r.SerialNumber,
                    NullIfBlank(r.Line),
                    NullIfBlank(r.PmEquipmentName),
                    NullIfBlank(r.TeamName),
                    NullIfBlank(r.ItemCategory),
                    usageBySerial.GetValueOrDefault(r.SerialNumber, 1),
                    r.ReceivedDate,
                    r.CurrentStatus,
                    // 합/부(2026-09-09 지시): 출고검사 당시의 판정을 그대로 따른다. 출고검사를 아직 안 했으면
                    // 비워 둔다("-"). 뒤쪽 파라미터 블록의 RESULT 열과 같은 값이라 그 열은 빼고 여기만 남겼다.
                    runByLot.GetValueOrDefault((r.Id, FiInspectionOper))?.Result switch
                    {
                        ProcessResult.Pass => "합",
                        ProcessResult.Fail => "부",
                        _ => null
                    },
                    r.ShipDate,
                    values);
            }).ToList(),
            columnGroups);
    }

    // 열 키: 화면이 Binding Path=Values[키]로 읽는다. WPF 인덱서 경로에 안전한 문자만 쓴다(영문/숫자/_).
    private static string ValueKey(int oper, string code, int index) =>
        $"{(oper == FiInspectionOper ? "FI" : "IN")}_{code.ToUpperInvariant()}_{index}";

    private static string InspectionKey(int oper, string field) =>
        $"{(oper == FiInspectionOper ? "FI" : "IN")}_INSP_{field}";

    // 2단 그룹 헤더 정의를 만든다. 입고 파라미터 그룹들 → 입고검사 → 출고 파라미터 그룹들 → 출고검사 순서
    // (2026-09-08 지시 #2). 검사 블록(DATE/TIME/작업자)은 파라미터와 달리 항상 같은 3열이라 고정이다.
    private static IReadOnlyList<LotHistoryColumnGroupDto> BuildColumnGroups(IEnumerable<(int Oper, string Code, int ValueCount)> columns)
    {
        var groups = new List<LotHistoryColumnGroupDto>();
        var ordered = columns.ToList();

        foreach (var oper in new[] { InInspectionOper, FiInspectionOper })
        {
            var prefix = oper == FiInspectionOper ? "FI" : "IN";
            var isOutbound = oper == FiInspectionOper;

            foreach (var group in ordered.Where(c => c.Oper == oper)
                         .GroupBy(c => GroupNameOf(c.Code))
                         .OrderBy(g => GroupIndexOf(g.First().Code)))
            {
                var isWeightGroup = string.Equals(group.Key, WeightGroup, StringComparison.Ordinal);
                var width = isWeightGroup ? WeightLeafWidth : DefaultLeafWidth;

                var leaves = new List<LotHistoryColumnLeafDto>();
                foreach (var column in group)
                {
                    if (column.ValueCount <= 1)
                    {
                        // 값이 하나면 열 이름은 파라미터 코드 그대로(예: Visual 그룹의 CHIP/CRACK/...).
                        leaves.Add(new LotHistoryColumnLeafDto(ValueKey(oper, column.Code, 0), column.Code.ToUpperInvariant(), width));
                    }
                    else
                    {
                        // 값이 여러 개면 A/B/C/D... 로 쪼갠다(예: Particle A~D). 그룹에 코드가 하나뿐이면
                        // 윗줄에 이미 코드 성격이 드러나므로 A/B/C/D만, 여러 개면 "코드 A"로 구분한다.
                        var single = group.Count() == 1;
                        for (var i = 0; i < column.ValueCount; i++)
                        {
                            var suffix = ((char)('A' + i)).ToString();
                            var title = single ? suffix : $"{column.Code.ToUpperInvariant()} {suffix}";
                            leaves.Add(new LotHistoryColumnLeafDto(ValueKey(oper, column.Code, i), title, width));
                        }
                    }
                }

                // 2026-09-09 지시: 출고검사 무게 바로 오른쪽 칸에 식각량(= 입고검사 무게 - 출고검사 무게).
                // 출고 Weight 그룹이 있을 때만 만든다 - 뺄 대상이 없으면 열도 의미가 없다.
                if (isOutbound && isWeightGroup)
                {
                    leaves.Add(new LotHistoryColumnLeafDto(EtchAmountKey, "식각량", WeightLeafWidth));
                }

                if (leaves.Count > 0)
                {
                    groups.Add(new LotHistoryColumnGroupDto($"{prefix} · {group.Key}", isOutbound, leaves));
                }
            }

            groups.Add(new LotHistoryColumnGroupDto($"{prefix} INSP", isOutbound, new[]
            {
                new LotHistoryColumnLeafDto(InspectionKey(oper, "DATE"), "DATE", 100),
                new LotHistoryColumnLeafDto(InspectionKey(oper, "TIME"), "TIME", DefaultLeafWidth),
                new LotHistoryColumnLeafDto(InspectionKey(oper, "WORKER"), "작업자", 92)
            }));
        }

        return groups;
    }

    public async Task<IReadOnlyList<LotListItemDto>> SearchLotCurrentAsync(ProcessHistorySearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Lots.AsNoTracking().AsQueryable();

        if (request.CustomerId is int customerId)
        {
            query = query.Where(l => l.Product.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(request.CleaningCode))
        {
            var cleaningCode = request.CleaningCode.Trim();
            query = query.Where(l => l.Product.CleaningCode.Contains(cleaningCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var itemCode = request.ItemCode.Trim();
            query = query.Where(l => l.Product.ItemCode.Contains(itemCode));
        }

        if (!string.IsNullOrWhiteSpace(request.ExportNumber))
        {
            var exportNumber = request.ExportNumber.Trim();
            query = query.Where(l => l.Registration != null && l.Registration.ExportNumber.Contains(exportNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            var productName = request.ProductName.Trim();
            query = query.Where(l => l.Product.ProductName.Contains(productName));
        }

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var serialNumber = request.SerialNumber.Trim();
            query = query.Where(l => l.SerialNumber.Contains(serialNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.ProcessName))
        {
            var processName = request.ProcessName.Trim();
            query = query.Where(l => l.CurrentProcessDefinition.ProcessName.Contains(processName));
        }

        if (request.Result is ProcessResult result)
        {
            // 그 LOT의 가장 최근(비무효) 이력의 판정을 기준으로 필터.
            query = query.Where(l => _context.ProcessHistories
                .Where(h => h.LotId == l.Id && !h.IsVoided)
                .OrderByDescending(h => h.StartedAt)
                .Select(h => h.Result)
                .FirstOrDefault() == result);
        }

        if (request.DateFrom is { } dateFrom)
        {
            query = query.Where(l => l.ReceivedDate >= dateFrom.Date);
        }

        if (request.DateTo is { } dateTo)
        {
            var exclusiveEnd = dateTo.Date.AddDays(1);
            query = query.Where(l => l.ReceivedDate < exclusiveEnd);
        }

        var rows = await query
            .OrderByDescending(l => l.ReceivedDate)
            .ThenByDescending(l => l.Id)
            .Take(request.Take)
            .Select(l => new
            {
                l.Id,
                l.LotNumber,
                l.Product.ProductCode,
                l.Product.ItemCode,
                l.Product.ProductName,
                l.SerialNumber,
                l.Product.CleaningCode,
                CustomerName = l.Product.Customer.CustomerName,
                l.ReceivedQuantity,
                CurrentProcessName = l.CurrentProcessDefinition.ProcessName,
                l.CurrentStatus,
                l.ReceivedDate,
                l.UpdatedAt,
                ExportNumber = l.Registration != null ? l.Registration.ExportNumber : null,
                Line = l.Registration != null ? l.Registration.Line : null,
                Comment = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.Remarks != null && h.Remarks != "")
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.Remarks)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new LotListItemDto(
                r.Id,
                r.LotNumber,
                r.ProductCode,
                r.ItemCode,
                r.ProductName,
                r.SerialNumber,
                r.CleaningCode,
                r.CustomerName,
                r.ReceivedQuantity,
                r.CurrentProcessName,
                r.CurrentStatus,
                false,
                r.ReceivedDate,
                r.UpdatedAt,
                r.UpdatedAt,
                r.ExportNumber,
                r.Line,
                null,
                null,
                null,
                null,
                null,
                null,
                r.Comment))
            .ToList();
    }
}
