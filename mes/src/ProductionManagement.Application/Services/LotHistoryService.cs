using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// LOT 한 건의 지나온 자취를 모아 주는 구현. "LOT 현황 조회" 화면이 쓴다.
// 공정 이력·수량 증감·검사값을 한 화면에서 볼 수 있게 시간순으로 엮는다.
public class LotHistoryService : ILotHistoryService
{
    // "고객출하" 공정의 OperCode - EfLotQueryRepository의 동일 상수와 같은 값(2026-08-19 피드백:
    // S/N 누적 이력의 "출고일자"를 이 공정의 완료 시각으로 판단한다).
    private const int ShippingOperCode = 8100;


    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<Customer, int> _customers;
    private readonly IRepository<Registration, int> _registrations;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<RecipeDefinition, int> _recipes;
    private readonly IRepository<InspectionRecord, int> _inspectionRecords;
    private readonly IRepository<ParameterDefinition, int> _parameterDefinitions;

    public LotHistoryService(
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<Customer, int> customers,
        IRepository<Registration, int> registrations,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<RecipeDefinition, int> recipes,
        IRepository<InspectionRecord, int> inspectionRecords,
        IRepository<ParameterDefinition, int> parameterDefinitions)
    {
        _lots = lots;
        _products = products;
        _customers = customers;
        _registrations = registrations;
        _processDefinitions = processDefinitions;
        _processHistories = processHistories;
        _recipes = recipes;
        _inspectionRecords = inspectionRecords;
        _parameterDefinitions = parameterDefinitions;
    }

    public async Task<LotHistoryHeaderDto> GetHeaderAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");
        var product = await _products.GetByIdAsync(lot.ProductId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");
        var customer = await _customers.GetByIdAsync(product.CustomerId, cancellationToken);
        var currentProcess = await _processDefinitions.GetByIdAsync(lot.CurrentProcessDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("공정 정의를 찾을 수 없습니다.");
        var registration = lot.RegistrationId is { } registrationId
            ? await _registrations.GetByIdAsync(registrationId, cancellationToken)
            : null;

        var currentHistories = await _processHistories.ListAsync(
            h => h.LotId == lotId && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
        var latest = currentHistories.OrderByDescending(h => h.StartedAt).FirstOrDefault();

        // RECIPE ID/RES ID는 세정(3000)/건조(4000) 공정에 있을 때만 확인 가능하게 한다 (2026-08-18 피드백).
        var showsRecipeAndRes = currentProcess.OperCode is 3000 or 4000;
        string? recipeCode = null;
        if (showsRecipeAndRes && latest?.RecipeDefinitionId is { } recipeDefinitionId)
        {
            var recipe = await _recipes.GetByIdAsync(recipeDefinitionId, cancellationToken);
            recipeCode = recipe?.Code;
        }

        return new LotHistoryHeaderDto(
            lot.Id,
            lot.LotNumber,
            lot.SerialNumber,
            registration?.ExportNumber,
            product.CleaningCode,
            product.ProductName,
            customer?.CustomerName ?? "-",
            registration?.Line,
            currentProcess.OperCode,
            currentProcess.ProcessName,
            recipeCode,
            showsRecipeAndRes ? latest?.EquipmentId : null,
            latest?.Comment,
            registration?.PmEquipmentName);
    }

    public async Task<IReadOnlyList<LotTransitionRowDto>> GetTransitionsBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        // 2026-08-31 피드백(#14): 이력 삭제(무효화)된 이력은 제품 로그에서도 함께 빠지게 한다(!IsVoided).
        var histories = (await _processHistories.ListAsync(h => h.Lot.SerialNumber == serialNumber && !h.IsVoided, cancellationToken))
            .OrderBy(h => h.StartedAt)
            .ThenBy(h => h.Id)
            .ToList();
        if (histories.Count == 0)
        {
            return Array.Empty<LotTransitionRowDto>();
        }

        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var recipes = (await _recipes.ListAllAsync(cancellationToken)).ToDictionary(r => r.Id);

        // "CREATE" 행은 이 Lot 하나만의 최초 이력이 아니라, 같은 S/N을 가진 각 LOT(=각 전산등록/입고
        // 사이클)마다 그 LOT 자신의 가장 이른 이력에 붙인다 - 안 그러면 전체 누적 이력에서 맨 처음 한
        // 건에만 CREATE가 붙고 두 번째 이후 등록 사이클은 전부 END로 잘못 보인다.
        var earliestIdByLot = histories
            .GroupBy(h => h.LotId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.StartedAt).ThenBy(h => h.Id).First().Id);

        var rows = new List<LotTransitionRowDto>();
        // 2026-09-04 피드백(B안): TRAN TIME을 각 전이 이벤트 시각(START=작업시작, END=작업완료)이 아니라
        // "그 공정에 진입(도착)한 시각"으로 통일해 표기한다. 이 데이터 모델은 진입 시각을 별도 컬럼으로
        // 저장하지 않으므로, 진입 시각 = "직전(시간순 바로 앞) 이력의 완료 시각"(= 이 공정으로 넘어온 순간)으로
        // 파생한다. 각 LOT 사이클의 첫 이력(CREATE)은 넘어온 직전 공정이 없으므로 그 사이클의 입고 시각
        // (StartedAt)을 진입 시각으로 쓴다. 같은 공정 시도의 START/END/HOLD/REWORK/SKIP 행은 모두 이 하나의
        // 진입 시각으로 통일된다(구분은 TRAN CODE로 유지). histories는 StartedAt→Id 순으로 정렬돼 있다.
        DateTime? previousCompletedAt = null;
        foreach (var history in histories)
        {
            processes.TryGetValue(history.ProcessDefinitionId, out var process);
            var operCode = process?.OperCode ?? 0;
            var operDesc = process?.ProcessName ?? "-";

            // RECIPE ID/RECIPE DESC/RES ID는 세정(3000)/건조(4000) 시도에서만 채운다 - 참조 화면도
            // 다른 OPER 행에서는 이 칸들을 비워 둔다 (2026-08-18 피드백).
            var showsRecipeAndRes = operCode is 3000 or 4000;
            string? recipeId = null;
            string? recipeDesc = null;
            if (showsRecipeAndRes && history.RecipeDefinitionId is { } recipeDefinitionId
                && recipes.TryGetValue(recipeDefinitionId, out var recipe))
            {
                recipeId = recipe.Code;
                recipeDesc = recipe.Description;
            }
            var resId = showsRecipeAndRes ? history.EquipmentId : null;

            // 이 이력의 공정 진입 시각: 사이클 첫 이력이면 입고 시각(StartedAt), 아니면 직전 이력 완료 시각.
            // (직전 이력이 아직 진행 중이라 완료 시각이 없으면 안전하게 이 이력의 StartedAt으로 대체.)
            var isCycleStart = history.Id == earliestIdByLot[history.LotId];
            var arrivedAt = isCycleStart ? history.StartedAt : (previousCompletedAt ?? history.StartedAt);

            if (history.CompletedAt is not { } completedAt)
            {
                // 아직 진행 중인 시도 - START 행 하나만 보인다.
                rows.Add(BuildRow(operCode, operDesc, "START", arrivedAt, history, recipeId, recipeDesc, resId));
                continue;
            }

            if (history.StartedAt == completedAt)
            {
                // Start TRAN이 없는 즉시처리형 공정(입고/입고검사/출고검사/포장 등)이거나 Lot 최초 생성
                // 이벤트 - 그 LOT 자신의 가장 이른 이력이면 CREATE, 아니면 END로 표시한다.
                var tranCode = isCycleStart ? "CREATE" : "END";
                rows.Add(BuildRow(operCode, operDesc, tranCode, arrivedAt, history, recipeId, recipeDesc, resId));
                previousCompletedAt = completedAt;
                continue;
            }

            rows.Add(BuildRow(operCode, operDesc, "START", arrivedAt, history, recipeId, recipeDesc, resId));

            var endTranCode = history.Status switch
            {
                LotStatus.Hold => "HOLD",
                LotStatus.Rework => "REWORK",
                LotStatus.Completed when history.Result == ProcessResult.Fail => "SKIP",
                _ => "END"
            };
            rows.Add(BuildRow(operCode, operDesc, endTranCode, arrivedAt, history, recipeId, recipeDesc, resId));
            previousCompletedAt = completedAt;
        }

        return rows;
    }

    public async Task<IReadOnlyList<LotCycleRowDto>> GetCyclesBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        var lots = (await _lots.ListAsync(l => l.SerialNumber == serialNumber, cancellationToken))
            .OrderBy(l => l.ReceivedDate)
            .ThenBy(l => l.Id)
            .ToList();
        if (lots.Count == 0)
        {
            return Array.Empty<LotCycleRowDto>();
        }

        var lotIds = lots.Select(l => l.Id).ToHashSet();
        var shippingProcess = (await _processDefinitions.ListAsync(p => p.OperCode == ShippingOperCode, cancellationToken)).FirstOrDefault();
        var outboundDateByLot = shippingProcess is null
            ? new Dictionary<int, DateTime>()
            : (await _processHistories.ListAsync(
                    h => h.ProcessDefinitionId == shippingProcess.Id && lotIds.Contains(h.LotId) && h.CompletedAt != null,
                    cancellationToken))
                .GroupBy(h => h.LotId)
                .ToDictionary(g => g.Key, g => g.Max(h => h.CompletedAt!.Value));

        // 비고: 이 사이클(Lot) 동안 작업자가 남긴 CMT_AETS 코멘트를 시간순으로 모은다(2026-08-19 피드백).
        var remarksByLot = (await _processHistories.ListAsync(
                h => lotIds.Contains(h.LotId) && h.Comment != null && h.Comment != string.Empty,
                cancellationToken))
            .OrderBy(h => h.StartedAt)
            .GroupBy(h => h.LotId)
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(h => h.Comment)));

        return lots
            .Select((lot, index) => new LotCycleRowDto(
                index + 1,
                lot.ReceivedDate,
                outboundDateByLot.TryGetValue(lot.Id, out var outboundDate) ? outboundDate : null,
                remarksByLot.GetValueOrDefault(lot.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<LotParameterRecordDto>> GetParameterRecordsBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        var records = (await _inspectionRecords.ListAsync(r => r.Lot.SerialNumber == serialNumber, cancellationToken))
            .OrderBy(r => r.RecordedAt)
            .ToList();
        if (records.Count == 0)
        {
            return Array.Empty<LotParameterRecordDto>();
        }

        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var parameters = (await _parameterDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);

        return records
            .Select(r =>
            {
                processes.TryGetValue(r.ProcessDefinitionId, out var process);
                parameters.TryGetValue(r.ParameterDefinitionId, out var parameter);
                return new LotParameterRecordDto(
                    process?.OperCode ?? 0,
                    process?.ProcessName ?? "-",
                    parameter?.Code ?? "-",
                    parameter?.Description ?? "-",
                    r.InputValue,
                    r.Comment,
                    r.RecordedAt);
            })
            .ToList();
    }

    public async Task<int?> FindLotIdAsync(string? lotNumber, string? serialNumber, string? exportNumber, CancellationToken cancellationToken = default)
    {
        var normalizedLotNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        var normalizedSerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        var normalizedExportNumber = string.IsNullOrWhiteSpace(exportNumber) ? null : exportNumber.Trim();

        if (normalizedLotNumber is null && normalizedSerialNumber is null && normalizedExportNumber is null)
        {
            return null;
        }

        // 입력된 조건은 전부 AND로 좁혀서 찾는다(LOT+S/N을 같이 넣으면 더 정확하게 찾을 수 있게) -
        // 여러 건이 걸리면(예: OUT NO 하나에 LOT 여러 개) 가장 최근에 갱신된 것을 대표로 반환한다.
        var matches = await _lots.ListAsync(l =>
            (normalizedLotNumber == null || l.LotNumber == normalizedLotNumber) &&
            (normalizedSerialNumber == null || l.SerialNumber == normalizedSerialNumber) &&
            (normalizedExportNumber == null || (l.Registration != null && l.Registration.ExportNumber == normalizedExportNumber)),
            cancellationToken);

        return matches.OrderByDescending(l => l.UpdatedAt).FirstOrDefault()?.Id;
    }

    // 2026-09-08 지시: "LOT 현황 조회" 화면의 검색을 칸 3개(LOT/S/N/반출번호)에서 통합 검색 한 칸으로
    // 바꾼다. 위 FindLotIdAsync가 조건을 AND로 좁히는 것과 달리, 여기서는 입력한 값 하나를 세 항목에
    // 각각 대조해 OR로 찾는다(작업자가 무엇을 입력했는지 고르지 않아도 되게). 여러 건이 걸리면 기존과
    // 동일하게 가장 최근에 갱신된 것을 대표로 반환한다.
    public async Task<int?> FindLotIdByKeywordAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        if (normalized is null)
        {
            return null;
        }

        var matches = await _lots.ListAsync(l =>
            l.LotNumber == normalized ||
            l.SerialNumber == normalized ||
            (l.Registration != null && l.Registration.ExportNumber == normalized),
            cancellationToken);

        return matches.OrderByDescending(l => l.UpdatedAt).FirstOrDefault()?.Id;
    }

    private static LotTransitionRowDto BuildRow(
        int operCode, string operDesc, string tranCode, DateTime tranTime, ProcessHistory history,
        string? recipeId, string? recipeDesc, string? resId)
        // USER ID/USER DESC는 별도 사번 체계가 없어 둘 다 Worker(Windows 계정)를 그대로 쓴다.
        => new(operCode, operDesc, tranCode, tranTime, history.Quantity, recipeId, recipeDesc, resId,
            history.Remarks, history.Comment, history.Worker, history.Worker);
}
