using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Infrastructure.Data;

// LOT 목록 조회 전용 저장소. 기본 저장소(EfRepository)와 달리 여러 테이블을 조인하고 필요한 컬럼만
// 골라 DTO로 바로 만들어 낸다 - 화면이 쓰는 목록은 엔티티 전체가 아니라 표에 보이는 값들이기 때문이다.
// OPER 화면 목록, 대시보드 목록, 통합 키워드 검색(LOT/S-N/품목코드/제품명)이 여기를 지난다.
public class EfLotQueryRepository : ILotQueryRepository
{
    private readonly ApplicationDbContext _context;

    public EfLotQueryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<LotListItemDto> Items, int TotalCount)> SearchAsync(LotSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Lots.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(l =>
                l.LotNumber.Contains(keyword) ||
                l.SerialNumber.Contains(keyword) ||
                l.Product.ProductCode.Contains(keyword) ||
                l.Product.ItemCode.Contains(keyword) ||
                l.Product.ProductName.Contains(keyword));
        }

        if (request.CustomerId is int customerId)
        {
            query = query.Where(l => l.Product.CustomerId == customerId);
        }

        if (request.ProductId is int productId)
        {
            query = query.Where(l => l.ProductId == productId);
        }

        if (request.ProcessDefinitionId is int processId)
        {
            query = query.Where(l => l.CurrentProcessDefinitionId == processId);
        }

        if (request.Status is LotStatus status)
        {
            query = query.Where(l => l.CurrentStatus == status);
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

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(l => l.ReceivedDate)
            .ThenByDescending(l => l.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(l => new
            {
                l.Id,
                l.ProductId,
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
                IsBatch = l.RepresentativeLotId != null || _context.Lots.Any(other => other.RepresentativeLotId == l.Id),
                l.ReceivedDate,
                l.UpdatedAt,
                l.Registration,
                // TatHours(직전 단계 체류시간)용: ProcessHistory 기준 현재/직전 공정 진입 시각.
                CurrentEnteredAt = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => (DateTime?)h.StartedAt)
                    .FirstOrDefault(),
                PreviousEnteredAt = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId != l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => (DateTime?)h.StartedAt)
                    .FirstOrDefault(),
                // 2026-08-20 "입·출고 현황 조회" 헤더를 OperView "공정 목록"과 동일하게 맞추기 위해
                // 추가 - SearchOperLotsAsync의 같은 이름 서브쿼리와 완전히 동일한 패턴.
                Worker = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.Worker)
                    .FirstOrDefault(),
                RecipeCode = _context.ProductRecipeAssignments
                    .Where(a => a.ProductId == l.ProductId && a.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .Select(a => a.RecipeDefinition.Code)
                    .FirstOrDefault(),
                EquipmentId = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.EquipmentId)
                    .FirstOrDefault(),
                // Comment - LOT 전체 공정 이력에서 가장 최근 비어있지 않은 메모(Remarks).
                Remarks = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.Remarks != null && h.Remarks != "")
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.Remarks)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows
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
                r.IsBatch,
                r.ReceivedDate,
                r.UpdatedAt,
                r.UpdatedAt,
                r.Registration != null ? r.Registration.ExportNumber : null,
                r.Registration != null ? r.Registration.Line : null,
                r.Registration != null ? r.Registration.ProcessLabel : null,
                r.CurrentEnteredAt is { } cur && r.PreviousEnteredAt is { } prev
                    ? Math.Round((cur - prev).TotalHours, 1)
                    : (double?)null,
                r.RecipeCode,
                r.EquipmentId,
                r.Worker,
                r.CurrentEnteredAt,
                r.Remarks,
                r.Registration != null ? r.Registration.PmEquipmentName : null))
            .ToList();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<OperLotItemDto>> SearchOperLotsAsync(OperLotSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Lots
            .AsNoTracking()
            // 2026-08-28 피드백(#4): 배치로 묶인 LOT은 대표 LOT 하나만 목록에 보이고, 묶인 나머지(멤버)는
            // 숨긴다. 멤버는 RepresentativeLotId가 채워진 LOT이다(대표/비배치는 null).
            .Where(l => l.RepresentativeLotId == null)
            .Where(l => l.CurrentProcessDefinitionId == request.ProcessDefinitionId);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(l =>
                l.LotNumber.Contains(keyword) ||
                l.SerialNumber.Contains(keyword) ||
                l.Product.ProductCode.Contains(keyword) ||
                l.Product.ItemCode.Contains(keyword) ||
                l.Product.ProductName.Contains(keyword));
        }

        if (request.CustomerId is int customerId)
        {
            query = query.Where(l => l.Product.CustomerId == customerId);
        }

        if (request.ProductId is int productId)
        {
            query = query.Where(l => l.ProductId == productId);
        }

        if (request.Status is LotStatus status)
        {
            query = query.Where(l => l.CurrentStatus == status);
        }
        else
        {
            // SHIP TRAN(8100 고객출하) 처리로 Completed된 Lot은 OPER 화면의 기본 작업 목록에서는 빠진다
            // ("출하 처리" - 사용자 지시, 2026-08-18). 이력 자체는 지우지 않으므로 이력 조회 화면에서는
            // 그대로 확인할 수 있다 (HistoryViewModel/EfProcessHistoryQueryRepository는 이 필터의 영향을 받지 않음).
            query = query.Where(l => l.CurrentStatus != LotStatus.Completed);
        }

        return await ProjectOperLotsAsync(query, cancellationToken);
    }

    // 2026-08-31 피드백(#3): 대시보드 카드 클릭 목록과 SearchOperLotsAsync가 같은 OperLotItemDto 투영을 공유한다.
    private async Task<IReadOnlyList<OperLotItemDto>> ProjectOperLotsAsync(IQueryable<Lot> query, CancellationToken cancellationToken)
    {
        var rows = await query
            .OrderByDescending(l => l.UpdatedAt)
            .ThenByDescending(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.ProductId,
                l.LotNumber,
                l.Product.ProductCode,
                l.Product.ItemCode,
                l.Product.ProductName,
                l.SerialNumber,
                l.Product.CleaningCode,
                CustomerName = l.Product.Customer.CustomerName,
                CustomerLineCode = l.Product.Customer.LineDefinition != null ? l.Product.Customer.LineDefinition.Code : null,
                l.ReceivedQuantity,
                l.CurrentStatus,
                IsBatch = l.RepresentativeLotId != null || _context.Lots.Any(other => other.RepresentativeLotId == l.Id),
                l.ReceivedDate,
                l.UpdatedAt,
                l.Registration,
                Worker = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.Worker)
                    .FirstOrDefault(),
                CurrentEnteredAt = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => (DateTime?)h.StartedAt)
                    .FirstOrDefault(),
                PreviousEnteredAt = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId != l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => (DateTime?)h.StartedAt)
                    .FirstOrDefault(),
                // 세정/건조처럼 제품별로 공정 레시피를 배정해둔 경우에만 값이 있다 (ProductRecipeAssignment).
                RecipeCode = _context.ProductRecipeAssignments
                    .Where(a => a.ProductId == l.ProductId && a.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .Select(a => a.RecipeDefinition.Code)
                    .FirstOrDefault(),
                // 2026-08-31 피드백(#6): 목록 표기는 레시피 명(Description).
                RecipeName = _context.ProductRecipeAssignments
                    .Where(a => a.ProductId == l.ProductId && a.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .Select(a => a.RecipeDefinition.Description)
                    .FirstOrDefault(),
                // 2026-09-03 피드백: End T/Over T 계산용 - 이 제품/공정에 배정된 레시피의 READ TIME(분).
                // 값이 있으면(=레시피 있는 공정, 세정/건조/Bake 등) Start/End/Over T를 표기한다.
                RecipeReadTimeMinutes = _context.ProductRecipeAssignments
                    .Where(a => a.ProductId == l.ProductId && a.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .Select(a => a.RecipeDefinition.ReadTimeMinutes)
                    .FirstOrDefault(),
                // 현재 이 공정에서 실제 진행 중인(또는 가장 최근) 시도가 어느 설비호기를 썼는지(수기 입력값).
                EquipmentId = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinitionId == l.CurrentProcessDefinitionId)
                    .OrderByDescending(h => h.StartedAt)
                    .Select(h => h.EquipmentId)
                    .FirstOrDefault(),
                l.RepresentativeLotId,
                RepresentativeLotNumber = l.RepresentativeLot != null ? l.RepresentativeLot.LotNumber : null,
                // 2026-09-01 피드백(#5): 비고 = 이 LOT 전체 이력 중 가장 최근의 비어있지 않은 코멘트.
                Comment = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.Comment != null && h.Comment != "")
                    .OrderByDescending(h => h.StartedAt).ThenByDescending(h => h.Id)
                    .Select(h => h.Comment)
                    .FirstOrDefault(),
                // 2026-09-01 피드백: OPER = 이 LOT의 현재 공정(위치) 이름.
                CurrentProcessName = l.CurrentProcessDefinition.ProcessName
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OperLotItemDto(
                r.Id,
                r.LotNumber,
                r.ProductCode,
                r.ItemCode,
                r.ProductName,
                r.SerialNumber,
                r.CleaningCode,
                r.CustomerName,
                r.CustomerLineCode,
                r.ReceivedQuantity,
                r.CurrentStatus,
                r.IsBatch,
                r.ReceivedDate,
                r.UpdatedAt,
                r.Worker,
                r.CurrentEnteredAt,
                r.Registration != null ? r.Registration.ExportNumber : null,
                r.Registration != null ? r.Registration.Line : null,
                r.Registration != null ? r.Registration.ProcessLabel : null,
                r.CurrentEnteredAt is { } cur && r.PreviousEnteredAt is { } prev
                    ? Math.Round((cur - prev).TotalHours, 1)
                    : (double?)null,
                r.RecipeCode,
                r.EquipmentId,
                r.RepresentativeLotId,
                r.RepresentativeLotNumber,
                r.ProductId,
                r.Registration != null ? r.Registration.PmEquipmentName : null,
                r.Registration != null ? r.Registration.TeamName : null,
                r.RecipeName,
                r.Comment,
                r.CurrentProcessName,
                r.RecipeReadTimeMinutes))
            .ToList();
    }

    // 2026-08-31 피드백(#3): 대시보드 KPI/WIP 카드 클릭 시 그 카테고리에 해당하는 제품 목록을 OperLotItemDto로
    // 돌려준다(SearchOperLotsAsync와 같은 투영 재사용). 배치 멤버는 제외(대표만).
    public async Task<IReadOnlyList<OperLotItemDto>> GetDashboardLotsAsync(DashboardLotCategory category, int? operCode, CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.Lots.AsNoTracking().Where(l => l.RepresentativeLotId == null);
        var today = DateTime.Today;

        if (category == DashboardLotCategory.LongWait)
        {
            // 장기대기는 공정별 지연 기준(설정값)을 넘긴 활성 LOT - GetLongWaitLotsAsync와 같은 계산이라
            // 대상 Id를 먼저 구한 뒤 공통 투영에 태운다.
            var delayHoursByCode = await GetLongWaitDelayHoursByProcessCodeAsync(cancellationToken);
            if (delayHoursByCode.Count == 0)
            {
                return Array.Empty<OperLotItemDto>();
            }
            var candidates = await baseQuery
                .Where(l => l.CurrentStatus == LotStatus.Waiting || l.CurrentStatus == LotStatus.InProgress)
                .Select(l => new { l.Id, ProcessCode = l.CurrentProcessDefinition.ProcessCode, l.UpdatedAt })
                .ToListAsync(cancellationToken);
            var now = DateTime.Now;
            var ids = candidates
                .Where(l => delayHoursByCode.TryGetValue(l.ProcessCode, out var h) && (now - l.UpdatedAt).TotalHours > h)
                .Select(l => l.Id)
                .ToHashSet();
            return await ProjectOperLotsAsync(baseQuery.Where(l => ids.Contains(l.Id)), cancellationToken);
        }

        if (category == DashboardLotCategory.TodayShipped)
        {
            // 당일 고객출하(8100) 완료된 LOT들.
            var tomorrow = today.AddDays(1);
            var shippedIds = await _context.ProcessHistories.AsNoTracking()
                .Where(h => h.ProcessDefinition.OperCode == ShippingOperCode
                            && h.CompletedAt != null && h.CompletedAt >= today && h.CompletedAt < tomorrow)
                .Select(h => h.LotId)
                .Distinct()
                .ToListAsync(cancellationToken);
            return await ProjectOperLotsAsync(baseQuery.Where(l => shippedIds.Contains(l.Id)), cancellationToken);
        }

        var query = category switch
        {
            DashboardLotCategory.TodayReceived => baseQuery.Where(l => l.ReceivedDate >= today && l.ReceivedDate < today.AddDays(1)),
            DashboardLotCategory.InProgress => baseQuery.Where(l => l.CurrentStatus == LotStatus.InProgress),
            DashboardLotCategory.Hold => baseQuery.Where(l => l.CurrentStatus == LotStatus.Hold),
            DashboardLotCategory.Rework => baseQuery.Where(l => l.CurrentStatus == LotStatus.Rework),
            DashboardLotCategory.ShippingWaiting => baseQuery.Where(l => l.CurrentProcessDefinition.OperCode == ShippingOperCode && l.CurrentStatus == LotStatus.Waiting),
            DashboardLotCategory.WipStage => baseQuery.Where(l => l.CurrentProcessDefinition.OperCode == (operCode ?? -1) && l.CurrentStatus != LotStatus.Completed),
            _ => baseQuery.Where(l => false)
        };
        return await ProjectOperLotsAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<BatchMemberDto>> GetBatchMembersAsync(int representativeLotId, CancellationToken cancellationToken = default)
    {
        return await _context.Lots
            .AsNoTracking()
            .Where(l => l.Id == representativeLotId || l.RepresentativeLotId == representativeLotId)
            .OrderByDescending(l => l.Id == representativeLotId) // 대표를 맨 위로
            .ThenBy(l => l.LotNumber)
            .Select(l => new BatchMemberDto(l.Id, l.LotNumber, l.SerialNumber, l.Id == representativeLotId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessWipDto>> GetProcessWipCountsAsync(CancellationToken cancellationToken = default)
    {
        // 여러 ProcessRoute(STANDARD/SIMPLE 등)에 걸쳐 같은 ProcessDefinition이 등장할 수 있으므로
        // ProcessRouteStep을 그대로 나열하지 않고 ProcessDefinitionId 기준으로 중복 제거한다
        // (CLAUDE.md "ProcessRoute를 여러 개 다루게 된 이후로 반드시 확인할 것" 참고).
        var stages = await _context.ProcessRouteSteps
            .AsNoTracking()
            .Select(s => new { s.ProcessDefinitionId, s.ProcessDefinition.ProcessName, s.ProcessDefinition.OperCode, s.ProcessDefinition.ProcessCode })
            .Distinct()
            .OrderBy(s => s.OperCode)
            .ToListAsync(cancellationToken);

        // 평균 체류시간/병목 여부(2026-08-19 Dashboard 리디자인)를 계산하려면 LOT별 UpdatedAt이 필요해서
        // GroupBy 집계 대신 활성 LOT을 그대로 가져온다 - GetDashboardSummaryAsync의 LongWait 계산과 같은
        // 방식으로, 비종결 상태(진행중 전체 WIP)만 DB에서 걸러서 가져오므로 전체 Memory Filtering은 아니다.
        var terminalStatuses = new[] { LotStatus.Completed, LotStatus.Cancelled, LotStatus.Void };
        var activeLots = await _context.Lots
            .AsNoTracking()
            .Where(l => !terminalStatuses.Contains(l.CurrentStatus))
            .Select(l => new { l.CurrentProcessDefinitionId, l.UpdatedAt })
            .ToListAsync(cancellationToken);

        var delayHoursByCode = await GetLongWaitDelayHoursByProcessCodeAsync(cancellationToken);
        var now = DateTime.Now;
        var lotsByStage = activeLots.ToLookup(l => l.CurrentProcessDefinitionId);

        return stages
            .Select(s =>
            {
                var lotsInStage = lotsByStage[s.ProcessDefinitionId].ToList();
                double? avgWaitHours = lotsInStage.Count == 0
                    ? null
                    : Math.Round(lotsInStage.Average(l => (now - l.UpdatedAt).TotalHours), 1);
                var isBottleneck = delayHoursByCode.TryGetValue(s.ProcessCode, out var delayHours)
                    && lotsInStage.Any(l => (now - l.UpdatedAt).TotalHours > delayHours);

                return new ProcessWipDto(s.ProcessDefinitionId, s.ProcessName, s.OperCode, lotsInStage.Count, avgWaitHours, isBottleneck);
            })
            .ToList();
    }

    // Dashboard 하단 "장기대기 LOT" 패널 - GetDashboardSummaryAsync의 LongWait 카운트를 낸 것과 같은
    // 기준으로 실제 LOT 행을 내려준다(2026-08-19 리디자인).
    public async Task<IReadOnlyList<LotListItemDto>> GetLongWaitLotsAsync(int take, CancellationToken cancellationToken = default)
    {
        var delayHoursByCode = await GetLongWaitDelayHoursByProcessCodeAsync(cancellationToken);
        if (delayHoursByCode.Count == 0)
        {
            return Array.Empty<LotListItemDto>();
        }

        var activeLots = await _context.Lots.AsNoTracking()
            .Where(l => l.CurrentStatus == LotStatus.Waiting || l.CurrentStatus == LotStatus.InProgress)
            .Select(l => new
            {
                l.Id,
                l.ProductId,
                l.LotNumber,
                l.Product.ProductCode,
                l.Product.ItemCode,
                l.Product.ProductName,
                l.SerialNumber,
                l.Product.CleaningCode,
                CustomerName = l.Product.Customer.CustomerName,
                l.ReceivedQuantity,
                CurrentProcessName = l.CurrentProcessDefinition.ProcessName,
                ProcessCode = l.CurrentProcessDefinition.ProcessCode,
                l.CurrentStatus,
                IsBatch = l.RepresentativeLotId != null || _context.Lots.Any(other => other.RepresentativeLotId == l.Id),
                l.ReceivedDate,
                l.UpdatedAt,
                l.Registration
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.Now;
        return activeLots
            .Where(l => delayHoursByCode.TryGetValue(l.ProcessCode, out var delayHours) && (now - l.UpdatedAt).TotalHours > delayHours)
            .OrderByDescending(l => (now - l.UpdatedAt).TotalHours)
            .Take(take)
            .Select(l => new LotListItemDto(
                l.Id, l.LotNumber, l.ProductCode, l.ItemCode, l.ProductName, l.SerialNumber, l.CleaningCode,
                l.CustomerName, l.ReceivedQuantity, l.CurrentProcessName, l.CurrentStatus, l.IsBatch,
                l.ReceivedDate, l.UpdatedAt, l.UpdatedAt,
                l.Registration != null ? l.Registration.ExportNumber : null,
                l.Registration != null ? l.Registration.Line : null,
                l.Registration != null ? l.Registration.ProcessLabel : null,
                null, null, null, null, null))
            .ToList();
    }

    // GetDashboardSummaryAsync(LongWait 카운트)/GetProcessWipCountsAsync(병목 여부)/GetLongWaitLotsAsync
    // (실제 목록)이 전부 같은 기준을 써야 셋이 서로 어긋나지 않는다 - 여기 한 곳에서만 읽는다.
    private async Task<Dictionary<string, double>> GetLongWaitDelayHoursByProcessCodeAsync(CancellationToken cancellationToken)
    {
        var delaySettings = await _context.SystemSettings.AsNoTracking()
            .Where(s => s.SettingKey.StartsWith("LongWait.") && s.SettingKey.EndsWith(".DelayHours"))
            .ToListAsync(cancellationToken);

        return delaySettings
            .Select(s => new { Code = s.SettingKey.Substring("LongWait.".Length, s.SettingKey.Length - "LongWait.".Length - ".DelayHours".Length), s.SettingValue })
            .Where(x => double.TryParse(x.SettingValue, out _))
            .ToDictionary(x => x.Code, x => double.Parse(x.SettingValue), StringComparer.OrdinalIgnoreCase);
    }

    private const int ShippingOperCode = 8100;

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var todayReceived = await _context.Lots.AsNoTracking()
            .CountAsync(l => l.ReceivedDate >= today && l.ReceivedDate < tomorrow, cancellationToken);

        var inProgress = await _context.Lots.AsNoTracking().CountAsync(l => l.CurrentStatus == LotStatus.InProgress, cancellationToken);
        var hold = await _context.Lots.AsNoTracking().CountAsync(l => l.CurrentStatus == LotStatus.Hold, cancellationToken);
        var rework = await _context.Lots.AsNoTracking().CountAsync(l => l.CurrentStatus == LotStatus.Rework, cancellationToken);

        var shippingWaiting = await _context.Lots.AsNoTracking()
            .CountAsync(l => l.CurrentProcessDefinition.OperCode == ShippingOperCode && l.CurrentStatus == LotStatus.Waiting, cancellationToken);

        // 장기대기: 현재 진행/대기 중인 LOT 중 현재 공정 체류시간이 그 공정의 DelayHours 기준을 넘은 수.
        // 활성 WIP만(진행중/대기) DB에서 걸러서 가져오므로 전체 Memory Filtering이 아니다 (CLAUDE.md 성능 원칙).
        var activeLots = await _context.Lots.AsNoTracking()
            .Where(l => l.CurrentStatus == LotStatus.Waiting || l.CurrentStatus == LotStatus.InProgress)
            .Select(l => new { l.CurrentProcessDefinition.ProcessCode, l.UpdatedAt })
            .ToListAsync(cancellationToken);

        var delayHoursByCode = await GetLongWaitDelayHoursByProcessCodeAsync(cancellationToken);

        var now = DateTime.Now;
        var longWait = activeLots.Count(l =>
            delayHoursByCode.TryGetValue(l.ProcessCode, out var delayHours) &&
            (now - l.UpdatedAt).TotalHours > delayHours);

        var (avgTat, completedCount) = await GetTatAggregateAsync(today.AddDays(-30), today, cancellationToken);

        // 2026-09-01 피드백(#10): 당일 고객출하(8100) 완료된 LOT 수(중복 제거).
        var todayShipped = await _context.ProcessHistories.AsNoTracking()
            .Where(h => h.ProcessDefinition.OperCode == ShippingOperCode
                        && h.CompletedAt != null && h.CompletedAt >= today && h.CompletedAt < tomorrow)
            .Select(h => h.LotId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new DashboardSummaryDto(todayReceived, inProgress, hold, rework, shippingWaiting, longWait, avgTat, completedCount, todayShipped);
    }

    public async Task<TatReportDto> GetTatReportAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        var completed = await QueryCompletedTatAsync(dateFrom, dateTo, cancellationToken);

        var items = completed
            .OrderByDescending(x => x.ShippedAt)
            .Select(x => new TatLotItemDto(
                x.LotId, x.LotNumber, x.CustomerName, x.ProductName, x.SerialNumber,
                x.ReceivedDate, x.ShippedAt, Math.Round((x.ShippedAt - x.ReceivedDate).TotalHours, 1)))
            .ToList();

        if (items.Count == 0)
        {
            return new TatReportDto(null, null, null, 0, items);
        }

        return new TatReportDto(
            Math.Round(items.Average(i => i.TatHours), 1),
            items.Max(i => i.TatHours),
            items.Min(i => i.TatHours),
            items.Count,
            items);
    }

    private async Task<(double? Avg, int Count)> GetTatAggregateAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken)
    {
        var completed = await QueryCompletedTatAsync(dateFrom, dateTo, cancellationToken);
        if (completed.Count == 0)
        {
            return (null, 0);
        }
        var avg = completed.Average(x => (x.ShippedAt - x.ReceivedDate).TotalHours);
        return (Math.Round(avg, 1), completed.Count);
    }

    private async Task<List<CompletedTatRow>> QueryCompletedTatAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken)
    {
        var exclusiveEnd = dateTo.Date.AddDays(1);

        // 고객출하(OperCode 8100)에 도달해서 완료된 LOT + 그 출하 공정의 완료 시각(ShippedAt).
        var rows = await _context.Lots.AsNoTracking()
            .Where(l => l.CurrentProcessDefinition.OperCode == ShippingOperCode && l.CurrentStatus == LotStatus.Completed)
            .Select(l => new
            {
                l.Id,
                l.LotNumber,
                CustomerName = l.Product.Customer.CustomerName,
                l.Product.ProductName,
                l.SerialNumber,
                l.ReceivedDate,
                ShippedAt = _context.ProcessHistories
                    .Where(h => h.LotId == l.Id && h.ProcessDefinition.OperCode == ShippingOperCode && h.CompletedAt != null)
                    .OrderByDescending(h => h.CompletedAt)
                    .Select(h => h.CompletedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.ShippedAt != null && r.ShippedAt.Value >= dateFrom.Date && r.ShippedAt.Value < exclusiveEnd)
            .Select(r => new CompletedTatRow(r.Id, r.LotNumber, r.CustomerName, r.ProductName, r.SerialNumber, r.ReceivedDate, r.ShippedAt!.Value))
            .ToList();
    }

    private sealed record CompletedTatRow(int LotId, string LotNumber, string CustomerName, string ProductName, string SerialNumber, DateTime ReceivedDate, DateTime ShippedAt);

    public async Task<LotDetailHeaderDto?> GetHeaderAsync(int lotId, CancellationToken cancellationToken = default)
    {
        return await _context.Lots
            .AsNoTracking()
            .Where(l => l.Id == lotId)
            .Select(l => new LotDetailHeaderDto(
                l.Id,
                l.LotNumber,
                l.Product.ProductCode,
                l.Product.ItemCode,
                l.Product.ProductName,
                l.Product.SerialNumber,
                l.Product.Customer.CustomerName,
                l.ReceivedQuantity,
                l.CurrentProcessDefinitionId,
                l.CurrentProcessDefinition.ProcessName,
                l.CurrentStatus,
                l.ReceivedDate))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
