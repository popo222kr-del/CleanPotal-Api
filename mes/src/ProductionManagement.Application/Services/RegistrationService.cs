using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Validators;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 전산등록 구현 - LOT이 태어나는 곳. 반출번호로 등록 건을 만들고, 품목 수량만큼 LOT을 찍어 낸다.
// LOT 번호와 S/N은 사용자가 입력하지 못하고 채번기를 통해서만 나온다.
// 제품은 품목코드가 아니라 세정코드로 찾는다(세정코드가 유일 키이기 때문).
// 여러 행을 한 번에 올릴 때는 행마다 따로 커밋해서, 한 줄이 틀려도 나머지는 등록된다.
public class RegistrationService : IRegistrationService
{
    private const string RegistrationProcessCode = "REGISTRATION";

    private readonly IRepository<Registration, int> _registrations;
    private readonly IRepository<Customer, int> _customers;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessRouteStep, int> _processRouteSteps;
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<QuantityTransaction, int> _quantityTransactions;
    private readonly ILotNumberGenerator _lotNumberGenerator;
    private readonly IExportNumberGenerator _exportNumberGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly ILotCertificateService _lotCertificateService;

    public RegistrationService(
        IRepository<Registration, int> registrations,
        IRepository<Customer, int> customers,
        IRepository<Product, int> products,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessRouteStep, int> processRouteSteps,
        IRepository<Lot, int> lots,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<QuantityTransaction, int> quantityTransactions,
        ILotNumberGenerator lotNumberGenerator,
        IExportNumberGenerator exportNumberGenerator,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        ILotCertificateService lotCertificateService)
    {
        _registrations = registrations;
        _customers = customers;
        _products = products;
        _processDefinitions = processDefinitions;
        _processRouteSteps = processRouteSteps;
        _lots = lots;
        _processHistories = processHistories;
        _quantityTransactions = quantityTransactions;
        _lotNumberGenerator = lotNumberGenerator;
        _exportNumberGenerator = exportNumberGenerator;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _lotCertificateService = lotCertificateService;
    }

    public async Task<RegistrationResultDto> CreateAsync(RegistrationCreateRequest request, CancellationToken cancellationToken = default)
    {
        var errors = RegistrationValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var customer = await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new ValidationException(new[] { "선택한 업체를 찾을 수 없습니다." });

        var product = await _products.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new ValidationException(new[] { "선택한 품목을 찾을 수 없습니다." });

        if (!product.IsActive)
        {
            throw new ValidationException(new[] { "중지된 품목입니다. 활성화된 품목만 등록할 수 있습니다." });
        }

        if (product.CustomerId != customer.Id)
        {
            throw new ValidationException(new[] { "선택한 품목이 선택한 업체 소속이 아닙니다." });
        }

        var routeSteps = (await _processRouteSteps.ListAsync(s => s.ProcessRouteId == request.ProcessRouteId, cancellationToken))
            .OrderBy(s => s.StepOrder)
            .ToList();

        if (routeSteps.Count < 2)
        {
            throw new InvalidOperationException("선택한 공정 경로에 단계가 2개 미만입니다. 최소 전산등록+입고 단계가 필요합니다.");
        }

        var registrationProcess = (await _processDefinitions.ListAllAsync(cancellationToken))
            .FirstOrDefault(p => p.ProcessCode == RegistrationProcessCode)
            ?? throw new InvalidOperationException("전산등록 공정 Master Data가 없습니다.");

        var firstStep = routeSteps[0];
        var secondStep = routeSteps[1];

        if (firstStep.ProcessDefinitionId != registrationProcess.Id)
        {
            throw new InvalidOperationException("선택한 공정 경로의 첫 단계가 전산등록이 아닙니다. 경로 구성을 확인하세요.");
        }

        string exportNumber;
        if (!string.IsNullOrWhiteSpace(request.ExportNumberOverride))
        {
            // 2026-08-26 피드백: 반출번호는 처음 등록되는 것부터 "-1"을 붙여 적용한다(같은 반출번호로 여러
            // S/N을 등록할 수 있게 -1, -2 ... 로 자동 증가). 중복이면 막지 않고 다음 순번을 찾는다.
            var baseNumber = request.ExportNumberOverride.Trim();
            var seq = 1;
            do
            {
                exportNumber = $"{baseNumber}-{seq}";
                seq++;
            }
            while (await _registrations.ExistsAsync(r => r.ExportNumber == exportNumber, cancellationToken));
        }
        else
        {
            exportNumber = await _exportNumberGenerator.NextAsync(customer.Id, customer.ExportPrefix, request.ShipDate, cancellationToken);
        }

        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        var registration = new Registration
        {
            Customer = customer,
            Product = product,
            ExportNumber = exportNumber,
            Line = request.Line,
            ProcessLabel = request.ProcessLabel,
            ProcessRouteId = request.ProcessRouteId,
            RequestedQuantity = request.Quantity,
            ShipDate = request.ShipDate,
            PmEquipmentName = request.PmEquipmentName,
            TeamName = request.TeamName,
            OrderNumber = request.OrderNumber,
            RegisteredBy = actor,
            RegisteredAt = now
        };
        await _registrations.AddAsync(registration, cancellationToken);

        // (Lot, LotNumber, SerialNumber)만 임시로 모아두고, lot.Id는 아래 SaveChangesAsync 이후에
        // 읽는다 - AddAsync 직후에는 아직 DB에 반영되지 않아 자동증가 PK가 배정되지 않은 상태(0)이다.
        var pendingLots = new List<(Lot Lot, string LotNumber, string SerialNumber)>();

        for (var i = 1; i <= request.Quantity; i++)
        {
            var lotNumber = await _lotNumberGenerator.NextAsync(request.LotCode, cancellationToken);
            var serialNumber = request.Quantity == 1 && !string.IsNullOrWhiteSpace(request.SerialNumberOverride)
                ? request.SerialNumberOverride.Trim()
                : $"{exportNumber}_{i}";

            var lot = new Lot
            {
                LotNumber = lotNumber,
                Product = product,
                ReceivedQuantity = 1,
                CurrentProcessDefinitionId = secondStep.ProcessDefinitionId,
                CurrentStatus = LotStatus.Waiting,
                ProcessRouteId = request.ProcessRouteId,
                Registration = registration,
                SerialNumber = serialNumber,
                // 2026-09-08 지시: 전산등록 시점 S/N을 "반출 S/N"으로 보존한다(이후 SerialNumber가 바뀌어도 유지).
                InitialSerialNumber = serialNumber,
                // 2026-09-03 피드백(#3): "AETS 입고"는 실제 전산등록(입고) 시각(시:분 포함)이어야 한다.
                // 기존엔 고객출고일(ShipDate, 날짜만)로 저장돼 시간이 00:00으로 누락됐다. 고객출고일은
                // Registration.ShipDate로 별도 보존된다.
                ReceivedDate = now,
                CreatedAt = now,
                CreatedBy = actor,
                UpdatedAt = now,
                UpdatedBy = actor
            };
            await _lots.AddAsync(lot, cancellationToken);

            // 1000(전산등록) 단계는 생성과 동시에 자동완료 처리 - 기존 RECEIVING이 Lot 생성 시
            // 자동완료되던 패턴(LotService.CreateAsync)과 동일한 방식을 1000번 단계에 적용한다.
            await _processHistories.AddAsync(new ProcessHistory
            {
                Lot = lot,
                ProcessDefinitionId = firstStep.ProcessDefinitionId,
                Worker = actor,
                StartedAt = now,
                CompletedAt = now,
                Status = LotStatus.Completed,
                Quantity = 1,
                Remarks = $"전산등록 (반출번호 {exportNumber})"
            }, cancellationToken);

            await _quantityTransactions.AddAsync(new QuantityTransaction
            {
                Lot = lot,
                TransactionType = QuantityTransactionType.Received,
                Quantity = 1,
                OccurredAt = now,
                RecordedBy = actor
            }, cancellationToken);

            pendingLots.Add((lot, lotNumber, serialNumber));
        }

        _auditLogger.Log("Registration.Create", nameof(Registration), exportNumber, actor,
            $"CustomerId={customer.Id}, ProductId={product.Id}, Quantity={request.Quantity}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 2026-08-27 병합 Phase 3: 제품에 성적서 양식이 등록돼 있으면 LOT별 성적서(제목=LOT번호)를 생성한다.
        // 실패해도 등록 자체는 성공 처리(성적서 생성은 best-effort).
        foreach (var p in pendingLots)
        {
            await _lotCertificateService.TryGenerateForLotAsync(p.Lot.Id, cancellationToken);
        }

        var createdLots = pendingLots
            .Select(p => new CreatedLotDto(p.Lot.Id, p.LotNumber, p.SerialNumber))
            .ToList();

        return new RegistrationResultDto(registration.Id, exportNumber, createdLots);
    }

    public async Task<IReadOnlyList<RegistrationBatchItemResultDto>> CreateManyAsync(IReadOnlyList<RegistrationCreateRequest> requests, CancellationToken cancellationToken = default)
    {
        var results = new List<RegistrationBatchItemResultDto>();

        for (var i = 0; i < requests.Count; i++)
        {
            try
            {
                var result = await CreateAsync(requests[i], cancellationToken);
                results.Add(new RegistrationBatchItemResultDto(i, true, null, result));
            }
            catch (ValidationException ex)
            {
                results.Add(new RegistrationBatchItemResultDto(i, false, string.Join(" / ", ex.Errors), null));
            }
            catch (Exception ex)
            {
                results.Add(new RegistrationBatchItemResultDto(i, false, ex.Message, null));
            }
        }

        return results;
    }

    public async Task<RegistrationResultDto> UpdateAsync(int registrationId, RegistrationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExportNumber))
        {
            throw new ValidationException(new[] { "반출번호를 입력하세요." });
        }

        var registration = await _registrations.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new InvalidOperationException("전산등록 레코드를 찾을 수 없습니다.");

        if (registration.ExportNumber != request.ExportNumber
            && await _registrations.ExistsAsync(r => r.ExportNumber == request.ExportNumber, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 사용 중인 반출번호입니다: {request.ExportNumber}" });
        }

        registration.ExportNumber = request.ExportNumber;
        registration.Line = request.Line;
        registration.ProcessLabel = request.ProcessLabel;
        _registrations.Update(registration);

        var actor = _currentUser.GetCurrentUser();
        _auditLogger.Log("Registration.Update", nameof(Registration), registration.ExportNumber, actor);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var lots = await _lots.ListAsync(l => l.RegistrationId == registrationId, cancellationToken);
        var createdLots = lots.Select(l => new CreatedLotDto(l.Id, l.LotNumber, l.SerialNumber)).ToList();

        return new RegistrationResultDto(registration.Id, registration.ExportNumber, createdLots);
    }

    public async Task<RegistrationEditDto?> GetByLotIdAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

        if (lot.RegistrationId is not { } registrationId)
        {
            return null;
        }

        var registration = await _registrations.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new InvalidOperationException("전산등록 레코드를 찾을 수 없습니다.");

        return new RegistrationEditDto(registration.Id, registration.ExportNumber, registration.Line, registration.ProcessLabel);
    }
}
