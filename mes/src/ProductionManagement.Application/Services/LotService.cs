using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Validators;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// LOT 전반 구현. 목록 검색, 대시보드 집계(KPI/공정별 WIP/장기대기/TAT), 상세 조회에
// 더해 S/N 수정·코멘트 수정·배치 묶기/풀기 같은 LOT 자체를 손보는 일을 담는다.
// 공정을 진행시키는 일(TRAN 실행)은 여기가 아니라 ProcessTransitionService 쪽이다.
public class LotService : ILotService
{
    private const string ReceivingProcessCode = "RECEIVING";
    private const string StandardRouteCode = "STANDARD";

    private readonly ILotQueryRepository _lotQueryRepository;
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<Customer, int> _customers;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessRoute, int> _processRoutes;
    private readonly IRepository<ProcessRouteStep, int> _processRouteSteps;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<QuantityTransaction, int> _quantityTransactions;
    private readonly ILotNumberGenerator _lotNumberGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;

    public LotService(
        ILotQueryRepository lotQueryRepository,
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<Customer, int> customers,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessRoute, int> processRoutes,
        IRepository<ProcessRouteStep, int> processRouteSteps,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<QuantityTransaction, int> quantityTransactions,
        ILotNumberGenerator lotNumberGenerator,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser)
    {
        _lotQueryRepository = lotQueryRepository;
        _lots = lots;
        _products = products;
        _customers = customers;
        _processDefinitions = processDefinitions;
        _processRoutes = processRoutes;
        _processRouteSteps = processRouteSteps;
        _processHistories = processHistories;
        _quantityTransactions = quantityTransactions;
        _lotNumberGenerator = lotNumberGenerator;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
    }

    public Task<(IReadOnlyList<LotListItemDto> Items, int TotalCount)> SearchAsync(LotSearchRequest request, CancellationToken cancellationToken = default)
        => _lotQueryRepository.SearchAsync(request, cancellationToken);

    public Task<IReadOnlyList<ProcessWipDto>> GetProcessWipCountsAsync(CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetProcessWipCountsAsync(cancellationToken);

    public Task<IReadOnlyList<OperLotItemDto>> GetDashboardLotsAsync(DashboardLotCategory category, int? operCode, CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetDashboardLotsAsync(category, operCode, cancellationToken);

    public Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetDashboardSummaryAsync(cancellationToken);

    public Task<IReadOnlyList<LotListItemDto>> GetLongWaitLotsAsync(int take, CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetLongWaitLotsAsync(take, cancellationToken);

    public Task<TatReportDto> GetTatReportAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
        => _lotQueryRepository.GetTatReportAsync(dateFrom, dateTo, cancellationToken);

    public async Task<LotDetailDto?> GetDetailAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var header = await _lotQueryRepository.GetHeaderAsync(lotId, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var processDefinitions = (await _processDefinitions.ListAllAsync(cancellationToken))
            .ToDictionary(p => p.Id);

        var routeSteps = (await _processRouteSteps.ListAllAsync(cancellationToken))
            .OrderBy(s => s.StepOrder)
            .ToList();

        var histories = (await _processHistories.ListAsync(h => h.LotId == lotId, cancellationToken))
            .OrderBy(h => h.StartedAt)
            .ToList();

        var timeline = new List<ProcessTimelineStepDto>();
        foreach (var step in routeSteps)
        {
            var processName = processDefinitions.TryGetValue(step.ProcessDefinitionId, out var def) ? def.ProcessName : "-";
            var latestForStep = histories.LastOrDefault(h => h.ProcessDefinitionId == step.ProcessDefinitionId);

            TimelineState state;
            if (step.ProcessDefinitionId == header.CurrentProcessDefinitionId)
            {
                state = header.CurrentStatus switch
                {
                    LotStatus.Hold => TimelineState.Hold,
                    LotStatus.Rework => TimelineState.Rework,
                    LotStatus.Completed => TimelineState.Completed,
                    _ => TimelineState.Active
                };
            }
            else if (latestForStep is { Status: LotStatus.Completed })
            {
                state = TimelineState.Completed;
            }
            else
            {
                state = TimelineState.Pending;
            }

            timeline.Add(new ProcessTimelineStepDto(step.StepOrder, processName, state));
        }

        var historyDtos = histories
            .OrderByDescending(h => h.StartedAt)
            .Select(h => new ProcessHistoryItemDto(
                processDefinitions.TryGetValue(h.ProcessDefinitionId, out var def) ? def.ProcessName : "-",
                h.Worker,
                h.StartedAt,
                h.CompletedAt,
                h.Status,
                h.Result,
                h.Quantity,
                h.DefectQuantity,
                h.AttemptNumber,
                h.Remarks))
            .ToList();

        return new LotDetailDto(header, timeline, historyDtos);
    }

    public async Task<LotListItemDto> CreateAsync(LotCreateRequest request, CancellationToken cancellationToken = default)
    {
        var errors = LotValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var product = await _products.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new ValidationException(new[] { "선택한 제품을 찾을 수 없습니다." });

        if (!product.IsActive)
        {
            throw new ValidationException(new[] { "중지된 제품입니다. 활성화된 제품만 입고 등록할 수 있습니다." });
        }

        var receivingProcess = (await _processDefinitions.ListAllAsync(cancellationToken))
            .FirstOrDefault(p => p.ProcessCode == ReceivingProcessCode)
            ?? throw new InvalidOperationException("입고 공정 Master Data가 없습니다. Sample Data/공정 설정을 확인하세요.");

        var standardRoute = (await _processRoutes.ListAllAsync(cancellationToken))
            .FirstOrDefault(r => r.RouteCode == StandardRouteCode)
            ?? throw new InvalidOperationException("표준 공정 경로(STANDARD) Master Data가 없습니다.");

        // 이 경로(직접 입고 등록)는 전산등록과 달리 사용자가 입력하는 LOT코드 소스가 없다 - 실제로는
        // WPF UI 어디에서도 호출되지 않는 경로라(전산등록이 유일한 LOT 생성 진입점) 고정값으로 채운다.
        var lotNumber = await _lotNumberGenerator.NextAsync("LT", cancellationToken);
        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        var lot = new Lot
        {
            LotNumber = lotNumber,
            Product = product,
            ReceivedQuantity = request.Quantity,
            CurrentProcessDefinition = receivingProcess,
            CurrentStatus = LotStatus.Completed,
            ProcessRouteId = standardRoute.Id,
            ReceivedDate = request.ReceivedDate,
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor
        };
        await _lots.AddAsync(lot, cancellationToken);

        await _processHistories.AddAsync(new ProcessHistory
        {
            Lot = lot,
            ProcessDefinition = receivingProcess,
            Worker = actor,
            StartedAt = request.ReceivedDate,
            CompletedAt = request.ReceivedDate,
            Status = LotStatus.Completed,
            Quantity = request.Quantity,
            Remarks = request.Remarks
        }, cancellationToken);

        await _quantityTransactions.AddAsync(new QuantityTransaction
        {
            Lot = lot,
            TransactionType = QuantityTransactionType.Received,
            Quantity = request.Quantity,
            OccurredAt = request.ReceivedDate,
            RecordedBy = actor
        }, cancellationToken);

        _auditLogger.Log("Lot.Create", nameof(Lot), lotNumber, actor,
            $"ProductId={product.Id}, Quantity={request.Quantity}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var customer = await _customers.GetByIdAsync(product.CustomerId, cancellationToken);

        return new LotListItemDto(
            lot.Id,
            lot.LotNumber,
            product.ProductCode,
            product.ItemCode,
            product.ProductName,
            lot.SerialNumber,
            product.CleaningCode,
            customer?.CustomerName ?? "-",
            lot.ReceivedQuantity,
            receivingProcess.ProcessName,
            lot.CurrentStatus,
            false,
            lot.ReceivedDate,
            lot.UpdatedAt,
            lot.UpdatedAt,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    public async Task UpdateSerialNumberAsync(int lotId, string newSerialNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newSerialNumber))
        {
            throw new ValidationException(new[] { "S/N을 입력하세요." });
        }

        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

        // 2026-09-03 피드백(#3): 출하 완료된 LOT은 수정할 수 없다(추적 무결성). 성적서(Document)는 별도로 변경 가능.
        EnsureNotShipped(lot);

        var actor = _currentUser.GetCurrentUser();
        lot.SerialNumber = newSerialNumber.Trim();
        lot.UpdatedAt = DateTime.Now;
        lot.UpdatedBy = actor;
        _lots.Update(lot);

        _auditLogger.Log("Lot.UpdateSerialNumber", nameof(Lot), lot.LotNumber, actor, $"NewSerialNumber={lot.SerialNumber}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // 2026-09-01 피드백(#2/#3): 현재 공정의 최신 이력 코멘트를 갱신한다(없으면 대기 이력을 하나 열어 담는다
    // - InspectionService.SaveAsync와 동일 패턴). 이 값이 OPER CMT_AETS/LOT 현황 조회의 해당 공정 코멘트와
    // 같은 필드라, 여기서 바꾸면 그쪽에도 동일하게 반영된다.
    public async Task UpdateCurrentCommentAsync(int lotId, string? comment, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");
        // 2026-09-03 피드백(#3): 출하 완료된 LOT은 코멘트도 수정 불가(성적서는 별도 변경 가능).
        EnsureNotShipped(lot);
        var actor = _currentUser.GetCurrentUser();
        var normalized = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        var currentHistories = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
        var latest = currentHistories.OrderByDescending(h => h.StartedAt).ThenByDescending(h => h.Id).FirstOrDefault();
        if (latest is not null)
        {
            latest.Comment = normalized;
            _processHistories.Update(latest);
        }
        else
        {
            await _processHistories.AddAsync(new ProcessHistory
            {
                Lot = lot,
                ProcessDefinitionId = lot.CurrentProcessDefinitionId,
                Worker = actor,
                StartedAt = DateTime.Now,
                Status = lot.CurrentStatus,
                Quantity = lot.ReceivedQuantity,
                AttemptNumber = currentHistories.Count + 1,
                Comment = normalized
            }, cancellationToken);
        }

        _auditLogger.Log("Lot.UpdateComment", nameof(Lot), lot.LotNumber, actor, "코멘트 수정");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // 2026-09-03 피드백(#3): 출하 완료(Completed) LOT은 정보 수정을 막는다(추적 무결성). 성적서(Document)는
    // DocumentService로 별도 관리되며 이 가드의 영향을 받지 않는다.
    private static void EnsureNotShipped(Lot lot)
    {
        if (lot.CurrentStatus == LotStatus.Completed)
        {
            throw new InvalidOperationException("출하 완료된 LOT은 수정할 수 없습니다. (성적서는 변경 가능)");
        }
    }

    // 세정(3000)/건조(4000) 공정에서 진행중이거나 HOLD인 LOT은 Batch/UnBatch 둘 다 대상이 될 수 없다
    // (2026-08-20 사용자 지시 - 실제로 설비에 적재/진행 중인 물리적 상태를 건드리지 않기 위함).
    private async Task EnsureNotInProgressOrHoldAsync(Lot lot, CancellationToken cancellationToken)
    {
        const int CleaningOperCode = 3000;
        const int DryingOperCode = 4000;

        if (lot.CurrentStatus is not (LotStatus.InProgress or LotStatus.Hold))
        {
            return;
        }

        var process = await _processDefinitions.GetByIdAsync(lot.CurrentProcessDefinitionId, cancellationToken);
        if (process is not null && (process.OperCode == CleaningOperCode || process.OperCode == DryingOperCode))
        {
            throw new ValidationException(new[]
            {
                $"LOT {lot.LotNumber}은(는) 세정/건조 진행중이거나 HOLD 상태라 Batch/UnBatch할 수 없습니다."
            });
        }
    }

    public async Task BatchAsync(IReadOnlyList<int> lotIds, CancellationToken cancellationToken = default)
    {
        var distinctIds = lotIds.Distinct().ToList();
        if (distinctIds.Count < 2)
        {
            throw new ValidationException(new[] { "Batch는 최소 2개 이상의 LOT을 선택해야 합니다." });
        }

        var lots = new List<Lot>();
        foreach (var lotId in distinctIds)
        {
            var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
                ?? throw new InvalidOperationException($"LOT(Id={lotId})를 찾을 수 없습니다.");
            lots.Add(lot);
        }

        var firstProductId = lots[0].ProductId;
        if (lots.Any(l => l.ProductId != firstProductId))
        {
            // Product.CleaningCode는 NOT NULL+UNIQUE고 Product.CustomerId도 고정이라, ProductId가 같다는
            // 것 하나만 확인하면 "세정코드 동일 + 업체명 동일" 조건을 그대로 충족한다.
            throw new ValidationException(new[] { "세정코드/업체가 다른 LOT은 같은 배치로 묶을 수 없습니다." });
        }

        foreach (var lot in lots)
        {
            await EnsureNotInProgressOrHoldAsync(lot, cancellationToken);

            var alreadyGrouped = lot.RepresentativeLotId is not null
                || await _lots.ExistsAsync(other => other.RepresentativeLotId == lot.Id, cancellationToken);
            if (alreadyGrouped)
            {
                throw new ValidationException(new[]
                {
                    $"LOT {lot.LotNumber}은(는) 이미 다른 배치에 속해 있습니다. 먼저 UnBatch한 뒤 다시 시도하세요."
                });
            }
        }

        // 대표 LOT = 가장 먼저 생성된 LOT. LotNumber 문자열 정렬(Ordinal)에 기대던 이전 방식은 LotNumber가
        // 항상 0-padding된 순증가 숫자였을 때만 "생성 순서 == 문자열 순서"가 성립했다 - 2026-08-21 LOT
        // 번호 형식이 "1{코드}{yyMMdd}{순번}P"로 바뀌면서 코드(임의의 2글자)가 날짜보다 앞에 오게 되어
        // 더 이상 문자열 정렬이 생성 순서를 보장하지 않는다(예: 다른 코드로 늦게 등록된 LOT의 문자열이
        // 알파벳상 앞설 수 있음). Id는 자동증가 PK라 항상 실제 생성 순서와 일치하므로 이걸로 대체한다.
        var representative = lots.OrderBy(l => l.Id).First();
        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        foreach (var lot in lots.Where(l => l.Id != representative.Id))
        {
            lot.RepresentativeLotId = representative.Id;
            lot.UpdatedAt = now;
            lot.UpdatedBy = actor;
            _lots.Update(lot);
        }

        _auditLogger.Log("Lot.Batch", nameof(Lot), representative.LotNumber, actor,
            $"MemberLotIds={string.Join(",", lots.Where(l => l.Id != representative.Id).Select(l => l.LotNumber))}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnbatchAsync(IReadOnlyList<int> lotIds, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        foreach (var lotId in lotIds.Distinct())
        {
            var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
                ?? throw new InvalidOperationException($"LOT(Id={lotId})를 찾을 수 없습니다.");

            await EnsureNotInProgressOrHoldAsync(lot, cancellationToken);

            var members = await _lots.ListAsync(other => other.RepresentativeLotId == lot.Id, cancellationToken);
            if (members.Count > 0)
            {
                // lot 자신이 대표 - 그룹 전체를 해체한다.
                foreach (var member in members)
                {
                    member.RepresentativeLotId = null;
                    member.UpdatedAt = now;
                    member.UpdatedBy = actor;
                    _lots.Update(member);
                }
            }
            else if (lot.RepresentativeLotId is not null)
            {
                // lot 자신이 멤버 - 본인만 그룹에서 빠지고 나머지는 유지된다.
                lot.RepresentativeLotId = null;
                lot.UpdatedAt = now;
                lot.UpdatedBy = actor;
                _lots.Update(lot);
            }

            _auditLogger.Log("Lot.Unbatch", nameof(Lot), lot.LotNumber, actor);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
