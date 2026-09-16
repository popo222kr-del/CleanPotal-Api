using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 검사 패널(OPER 화면 우측의 IN INSP / FI INSP 표) 구현.
// GetPanelAsync는 이 제품·이 공정에 배정된 파라미터만 골라 표를 만들어 준다 - 그래서 세정·건조처럼
// 검사가 없는 공정에서는 빈 패널이 온다. SaveAsync는 입력값을 InspectionRecord에 기록하며,
// 같은 (LOT, 공정, 파라미터)에 다시 저장하면 덮어쓴다.
public class InspectionService : IInspectionService
{
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<Registration, int> _registrations;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<ProductRecipeAssignment, int> _productRecipes;
    private readonly IRepository<RecipeDefinition, int> _recipes;
    private readonly IRepository<ProductParameterAssignment, int> _productParameters;
    private readonly IRepository<ParameterDefinition, int> _parameters;
    private readonly IRepository<InspectionRecord, int> _inspectionRecords;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserProvider _currentUser;
    private readonly ICertificateFillService _certificateFillService;

    public InspectionService(
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<Registration, int> registrations,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<ProductRecipeAssignment, int> productRecipes,
        IRepository<RecipeDefinition, int> recipes,
        IRepository<ProductParameterAssignment, int> productParameters,
        IRepository<ParameterDefinition, int> parameters,
        IRepository<InspectionRecord, int> inspectionRecords,
        IUnitOfWork unitOfWork,
        ICurrentUserProvider currentUser,
        ICertificateFillService certificateFillService)
    {
        _lots = lots;
        _products = products;
        _processDefinitions = processDefinitions;
        _registrations = registrations;
        _processHistories = processHistories;
        _productRecipes = productRecipes;
        _recipes = recipes;
        _productParameters = productParameters;
        _parameters = parameters;
        _inspectionRecords = inspectionRecords;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _certificateFillService = certificateFillService;
    }

    public async Task<InspectionPanelDto> GetPanelAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");
        var product = await _products.GetByIdAsync(lot.ProductId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");
        var currentProcess = await _processDefinitions.GetByIdAsync(lot.CurrentProcessDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException("공정 정의를 찾을 수 없습니다.");

        var registration = lot.RegistrationId is { } registrationId
            ? await _registrations.GetByIdAsync(registrationId, cancellationToken)
            : null;

        var currentHistories = await _processHistories.ListAsync(
            h => h.LotId == lotId && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
        var latestCurrentHistory = currentHistories.OrderByDescending(h => h.StartedAt).FirstOrDefault();

        // CLN COUNT = "S/N 기준 입고 횟수" - 같은 물리적 부품(쿼츠 보트 등 재사용품)이 S/N을 그대로 유지한 채
        // 여러 번 전산등록/입고될 수 있어, 현재 공정 시도 횟수(AttemptNumber)가 아니라 이 S/N을 가진 Lot이
        // 전체 시스템에 몇 건 존재하는지로 센다(2026-08-18 사용자 확정 - 신규 요구사항).
        var clnCount = (await _lots.ListAsync(l => l.SerialNumber == lot.SerialNumber, cancellationToken)).Count;

        var (recipeDefinitionId, recipeCode) = await ResolveRecipeAsync(lot.ProductId, lot.CurrentProcessDefinitionId, cancellationToken);

        // 2026-08-26: 파라미터는 이제 제품별(ParameterDefinition.ProductId)이고 OPER별로 구분한다.
        // IN INSP 표는 이 제품의 2100(입고검사) 파라미터, FI INSP 표는 7000(출고검사) 파라미터를 쓴다.
        var productParams = (await _parameters.ListAsync(p => p.ProductId == lot.ProductId && p.IsActive, cancellationToken))
            .OrderBy(p => p.SortOrder)
            .ToList();
        var inInspParams = productParams.Where(p => p.Oper == "2100").ToList();
        var fiInspParams = productParams.Where(p => p.Oper == "7000").ToList();

        var incomingProcess = (await _processDefinitions.ListAsync(p => p.OperCode == 2100, cancellationToken)).FirstOrDefault();
        var outgoingProcess = (await _processDefinitions.ListAsync(p => p.OperCode == 7000, cancellationToken)).FirstOrDefault();

        var showsInInsp = currentProcess.OperCode is 2100 or 7000;
        var showsFiInsp = currentProcess.OperCode == 7000;
        var inInspEditable = currentProcess.OperCode == 2100;

        var inInspRows = showsInInsp && incomingProcess is not null
            ? await BuildRowsAsync(lotId, incomingProcess.Id, inInspParams, cancellationToken)
            : Array.Empty<InspectionParameterRowDto>();

        var fiInspRows = showsFiInsp && outgoingProcess is not null
            ? await BuildRowsAsync(lotId, outgoingProcess.Id, fiInspParams, cancellationToken)
            : Array.Empty<InspectionParameterRowDto>();

        // FI INSP 표는 이제 IN INSP(2100) 값을 "IN VALUE" 참고 컬럼으로 같이 보여준다(2026-08-19
        // 피드백: "IN VALUE (입고검사 데이터를 끌고옴)") - 별도 화면 섹션 대신 같은 행에 합쳐서 보여준다.
        if (showsFiInsp && fiInspRows.Count > 0)
        {
            // 2100/7000이 이제 별개 파라미터 행(Id 다름)이라 참고 IN VALUE는 코드로 매칭한다.
            var inValueByCode = inInspRows
                .GroupBy(r => r.Code)
                .ToDictionary(g => g.Key, g => g.First().InputValue);
            fiInspRows = fiInspRows
                .Select(r => r with { ReferenceInputValue = inValueByCode.GetValueOrDefault(r.Code) })
                .ToList();
        }

        // 2026-08-31 피드백(#15): 코멘트 칸에 기입한 내용이 공정을 넘어가도 계속 남아 있어야 한다.
        // 현재 OPER의 코멘트가 비어 있으면(방금 이동) 이 LOT의 가장 최근 코멘트를 그대로 이어서 보여준다.
        var allHistories = await _processHistories.ListAsync(h => h.LotId == lotId, cancellationToken);
        var latestComment = allHistories
            .Where(h => !string.IsNullOrWhiteSpace(h.Comment))
            .OrderByDescending(h => h.StartedAt).ThenByDescending(h => h.Id)
            .Select(h => h.Comment)
            .FirstOrDefault();
        var cmtAets = string.IsNullOrWhiteSpace(latestCurrentHistory?.Comment)
            ? latestComment
            : latestCurrentHistory!.Comment;

        return new InspectionPanelDto(
            lot.Id,
            lot.LotNumber,
            product.CleaningCode,
            product.ProductName,
            lot.SerialNumber,
            clnCount,
            recipeDefinitionId,
            recipeCode,
            registration?.PmEquipmentName,
            latestCurrentHistory?.EquipmentId,
            cmtAets,
            showsInInsp,
            inInspEditable,
            showsFiInsp,
            inInspRows,
            fiInspRows,
            null);
    }

    public async Task SaveAsync(SaveInspectionPanelRequest request, CancellationToken cancellationToken = default)
    {
        var lot = await _lots.GetByIdAsync(request.LotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");
        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        var currentHistories = await _processHistories.ListAsync(
            h => h.LotId == lot.Id && h.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken);
        var latest = currentHistories.OrderByDescending(h => h.StartedAt).FirstOrDefault();
        if (latest is not null)
        {
            latest.EquipmentId = request.ResId;
            latest.Comment = request.CmtAets;
        }
        else if (!string.IsNullOrWhiteSpace(request.ResId) || !string.IsNullOrWhiteSpace(request.CmtAets) || request.RecipeDefinitionId is not null)
        {
            // 작업시작(Start) TRAN을 누르기 전에 설비명(RES ID)/코멘트를 먼저 기입하는 경우 - 이 시점엔 아직
            // 이 OPER의 ProcessHistory가 하나도 없어 값을 저장할 곳이 없었다(2026-08-19 버그: TRAN 실행 시
            // 기입값이 사라짐 - 세정/건조 재현). Waiting 상태의 이력을 미리 하나 열어 값을 담아두면, 이후
            // OperActionService.ExecuteStartAsync가 이 열린 이력을 그대로 재사용하면서 값을 이어간다.
            latest = new ProcessHistory
            {
                Lot = lot,
                ProcessDefinitionId = lot.CurrentProcessDefinitionId,
                Worker = actor,
                StartedAt = now,
                Status = LotStatus.Waiting,
                Quantity = lot.ReceivedQuantity,
                AttemptNumber = currentHistories.Count + 1,
                EquipmentId = request.ResId,
                Comment = request.CmtAets
            };
            await _processHistories.AddAsync(latest, cancellationToken);
        }

        if (request.RecipeDefinitionId is { } recipeDefinitionId)
        {
            // 이 시도(Attempt)에 실제로 쓴 레시피 스냅샷을 이력에도 남긴다 - "LOT 현황 조회"에서 매 시도별
            // RECIPE ID를 보여주려면 ProductRecipeAssignment(제품의 "현재 기본값")만으로는 부족하다
            // (2026-08-18 피드백).
            if (latest is not null)
            {
                latest.RecipeDefinitionId = recipeDefinitionId;
            }

            // 세정/건조 OPER 화면에서 레시피를 고르는 것은 관리자 전용 "제품 셋업 > 기준정보" 편집이 아니라
            // 생산 현장에서 매 시도(Attempt)마다 하는 정상 업무라, ProductReferenceDataService의 관리자
            // 권한 체크(AdminProduct)를 거치지 않고 여기서 직접 배정을 갱신한다 (CLAUDE.md 10번: 공정
            // 관련 기능은 Role과 무관하게 전원 허용).
            var existingAssignment = (await _productRecipes.ListAsync(
                a => a.ProductId == lot.ProductId && a.ProcessDefinitionId == lot.CurrentProcessDefinitionId, cancellationToken)).FirstOrDefault();
            if (existingAssignment is not null)
            {
                existingAssignment.RecipeDefinitionId = recipeDefinitionId;
            }
            else
            {
                await _productRecipes.AddAsync(new ProductRecipeAssignment
                {
                    ProductId = lot.ProductId,
                    ProcessDefinitionId = lot.CurrentProcessDefinitionId,
                    RecipeDefinitionId = recipeDefinitionId
                }, cancellationToken);
            }
        }

        foreach (var input in request.ParameterInputs)
        {
            var existing = (await _inspectionRecords.ListAsync(
                r => r.LotId == lot.Id && r.ProcessDefinitionId == lot.CurrentProcessDefinitionId && r.ParameterDefinitionId == input.ParameterDefinitionId,
                cancellationToken)).FirstOrDefault();

            if (existing is not null)
            {
                existing.InputValue = input.InputValue;
                existing.Comment = input.Comment;
                existing.RecordedAt = now;
                existing.RecordedBy = actor;
            }
            else
            {
                await _inspectionRecords.AddAsync(new InspectionRecord
                {
                    LotId = lot.Id,
                    ProcessDefinitionId = lot.CurrentProcessDefinitionId,
                    ParameterDefinitionId = input.ParameterDefinitionId,
                    InputValue = input.InputValue,
                    Comment = input.Comment,
                    RecordedAt = now,
                    RecordedBy = actor
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 2026-08-31 피드백(#10): 검사 저장/공정 이동마다 성적서를 열고 닫지 않는다. 성적서 값 반영은
        // 입고검사(2100)·출고검사(7000)를 "완료(End)"할 때만 수행한다(OperActionService.ExecuteTranAsync).
    }

    private async Task<(int? RecipeDefinitionId, string? RecipeCode)> ResolveRecipeAsync(int productId, int processDefinitionId, CancellationToken cancellationToken)
    {
        var assignments = await _productRecipes.ListAsync(
            a => a.ProductId == productId && a.ProcessDefinitionId == processDefinitionId, cancellationToken);
        var assignment = assignments.FirstOrDefault();
        if (assignment is null)
        {
            return (null, null);
        }

        var recipe = await _recipes.GetByIdAsync(assignment.RecipeDefinitionId, cancellationToken);
        return (assignment.RecipeDefinitionId, recipe?.Code);
    }

    private async Task<IReadOnlyList<InspectionParameterRowDto>> BuildRowsAsync(
        int lotId, int processDefinitionId, IReadOnlyList<ParameterDefinition> parameterDefs, CancellationToken cancellationToken)
    {
        var records = (await _inspectionRecords.ListAsync(
            r => r.LotId == lotId && r.ProcessDefinitionId == processDefinitionId, cancellationToken))
            .ToDictionary(r => r.ParameterDefinitionId);

        return parameterDefs
            .Select(p =>
            {
                records.TryGetValue(p.Id, out var record);
                return new InspectionParameterRowDto(
                    p.Id, p.Code, p.Description, p.MinValue, p.MaxValue,
                    record?.InputValue, record?.Comment, ReferenceInputValue: null,
                    ParameterType: p.ParameterType, ValueCount: p.ValueCount);
            })
            .ToList();
    }
}
