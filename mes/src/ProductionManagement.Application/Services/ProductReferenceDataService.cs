using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 제품별 기준정보(검사 파라미터·레시피·기본 LINE) 관리 구현. "셋업 > 제품 셋업" 우측 카드가 쓴다.
// 여기서 배정한 파라미터가 곧 OPER 화면 검사 표의 행이 되고, 세정 이력 조회의 열이 된다
// - 화면에 열을 추가하려면 코드가 아니라 이 마스터를 고치면 된다.
// 다른 세정코드의 설정을 통째로 가져오는 복사 기능(CopyParameters/CopyProductRecipes)도 담는다.
public class ProductReferenceDataService : IProductReferenceDataService
{
    private readonly IRepository<LineDefinition, int> _lines;
    private readonly IRepository<RecipeDefinition, int> _recipes;
    private readonly IRepository<ParameterDefinition, int> _parameters;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<ProcessTransitionDefinition, int> _transitions;
    private readonly IRepository<ProductRecipeAssignment, int> _productRecipes;
    private readonly IRepository<ProductParameterAssignment, int> _productParameters;
    private readonly IRepository<Product, int> _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public ProductReferenceDataService(
        IRepository<LineDefinition, int> lines,
        IRepository<RecipeDefinition, int> recipes,
        IRepository<ParameterDefinition, int> parameters,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<ProcessTransitionDefinition, int> transitions,
        IRepository<ProductRecipeAssignment, int> productRecipes,
        IRepository<ProductParameterAssignment, int> productParameters,
        IRepository<Product, int> products,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _lines = lines;
        _recipes = recipes;
        _parameters = parameters;
        _processDefinitions = processDefinitions;
        _transitions = transitions;
        _productRecipes = productRecipes;
        _productParameters = productParameters;
        _products = products;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<LineOptionDto>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        var lines = await _lines.ListAsync(l => l.IsActive, cancellationToken);
        return lines.OrderBy(l => l.SortOrder).Select(l => new LineOptionDto(l.Id, l.Code, l.Description)).ToList();
    }

    public async Task<IReadOnlyList<RecipeOptionDto>> GetRecipesAsync(CancellationToken cancellationToken = default)
    {
        var recipes = await _recipes.ListAsync(r => r.IsActive, cancellationToken);
        return recipes.OrderBy(r => r.Code).Select(r => new RecipeOptionDto(r.Id, r.Code, r.Description, r.OperCode, r.ReadTimeMinutes)).ToList();
    }

    public async Task<IReadOnlyList<ParameterOptionDto>> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        var parameters = await _parameters.ListAsync(p => p.IsActive, cancellationToken);
        return parameters.OrderBy(p => p.Oper).ThenBy(p => p.SortOrder).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ParameterOptionDto>> GetAllParametersAsync(CancellationToken cancellationToken = default)
    {
        var parameters = await _parameters.ListAllAsync(cancellationToken);
        return parameters.OrderBy(p => p.Oper).ThenBy(p => p.SortOrder).Select(ToDto).ToList();
    }

    // 2026-08-26: 제품별 파라미터. 해당 제품(ProductId)의 검사 항목만(미사용 포함) 반환한다.
    public async Task<IReadOnlyList<ParameterOptionDto>> GetProductParametersAsync(int productId, CancellationToken cancellationToken = default)
    {
        var parameters = await _parameters.ListAsync(p => p.ProductId == productId, cancellationToken);
        return parameters.OrderBy(p => p.Oper).ThenBy(p => p.SortOrder).Select(ToDto).ToList();
    }

    private static ParameterOptionDto ToDto(ParameterDefinition p) => new(
        p.Id, p.Code, p.Description, p.ParameterType,
        p.Oper, p.ValueCount, p.MinValue, p.MaxValue, p.Unit, p.IsActive, p.ProductId, p.CertificateLabel);

    public async Task<int> CreateParameterAsync(ParameterUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        ValidateParameter(request);

        // (ProductId, Code, Oper) 복합 유일 - 같은 제품 안에서 코드+OPER가 이미 있으면 막는다.
        if (await _parameters.ExistsAsync(p => p.ProductId == request.ProductId && p.Code == request.Code && p.Oper == request.Oper, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 파라미터입니다: {request.Code} (OPER {request.Oper ?? "-"})" });
        }

        var maxSort = (await _parameters.ListAllAsync(cancellationToken)).Select(p => (int?)p.SortOrder).Max() ?? 0;
        var entity = new ParameterDefinition
        {
            Code = request.Code.Trim(),
            Description = request.Description.Trim(),
            ParameterType = request.ParameterType,
            Oper = string.IsNullOrWhiteSpace(request.Oper) ? null : request.Oper.Trim(),
            ValueCount = request.ValueCount,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim(),
            IsActive = request.IsActive,
            ProductId = request.ProductId,
            CertificateLabel = string.IsNullOrWhiteSpace(request.CertificateLabel) ? null : request.CertificateLabel.Trim(),
            SortOrder = maxSort + 1
        };
        await _parameters.AddAsync(entity, cancellationToken);
        _auditLogger.Log("Parameter.Create", nameof(ParameterDefinition), $"{entity.Code}/{entity.Oper}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateParameterAsync(int parameterId, ParameterUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        ValidateParameter(request);

        var entity = await _parameters.GetByIdAsync(parameterId, cancellationToken)
            ?? throw new InvalidOperationException("파라미터를 찾을 수 없습니다.");

        if (await _parameters.ExistsAsync(p => p.ProductId == entity.ProductId && p.Code == request.Code && p.Oper == request.Oper && p.Id != parameterId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 파라미터입니다: {request.Code} (OPER {request.Oper ?? "-"})" });
        }

        entity.Code = request.Code.Trim();
        entity.Description = request.Description.Trim();
        entity.ParameterType = request.ParameterType;
        entity.Oper = string.IsNullOrWhiteSpace(request.Oper) ? null : request.Oper.Trim();
        entity.ValueCount = request.ValueCount;
        entity.MinValue = request.MinValue;
        entity.MaxValue = request.MaxValue;
        entity.Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim();
        entity.IsActive = request.IsActive;
        entity.CertificateLabel = string.IsNullOrWhiteSpace(request.CertificateLabel) ? null : request.CertificateLabel.Trim();

        _parameters.Update(entity);
        _auditLogger.Log("Parameter.Update", nameof(ParameterDefinition), $"{entity.Code}/{entity.Oper}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteParameterAsync(int parameterId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var entity = await _parameters.GetByIdAsync(parameterId, cancellationToken)
            ?? throw new InvalidOperationException("파라미터를 찾을 수 없습니다.");

        // 제품에 배정된 파라미터라면 배정도 함께 제거한다.
        var assignments = await _productParameters.ListAsync(a => a.ParameterDefinitionId == parameterId, cancellationToken);
        foreach (var a in assignments)
        {
            _productParameters.Remove(a);
        }
        _parameters.Remove(entity);
        _auditLogger.Log("Parameter.Delete", nameof(ParameterDefinition), $"{entity.Code}/{entity.Oper}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // 2026-08-26: PARAMETER 선택용 카탈로그. ProductId가 없는 참조 행(파라미터 정의 마스터)에서 코드별
    // 1건씩(설명 포함) 추린다. 제품별 파라미터는 여기서 코드를 골라 만든다.
    public async Task<IReadOnlyList<ParameterCatalogDto>> GetParameterCatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _parameters.ListAsync(p => p.ProductId == null, cancellationToken);
        return catalog
            .GroupBy(p => p.Code)
            .Select(g => new ParameterCatalogDto(g.Key, g.OrderBy(x => x.SortOrder).First().Description))
            .OrderBy(c => c.Code)
            .ToList();
    }

    // OPER 선택용 공정 목록(활성 공정, OperCode 순).
    public async Task<IReadOnlyList<OperOptionDto>> GetOperOptionsAsync(CancellationToken cancellationToken = default)
    {
        var processes = await _processDefinitions.ListAsync(p => p.IsActive, cancellationToken);
        return processes.OrderBy(p => p.OperCode).Select(p => new OperOptionDto(p.OperCode, p.ProcessName)).ToList();
    }

    // 2026-08-26 COPY 탭: 원본 세정코드(sourceCleaningCode)의 파라미터를 대상 제품(targetProductId)으로
    // 통째로 복사한다. 매번 세정코드마다 파라미터를 하나씩 넣기 번거로워, 이미 설정된 세정코드에서
    // 불러오는 방식(피드백). 대상에 이미 같은 (Code, Oper)가 있으면 건너뛰고, 새로 만든 개수를 반환한다.
    public async Task<int> CopyParametersAsync(int targetProductId, string sourceCleaningCode, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var code = (sourceCleaningCode ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            throw new ValidationException(new[] { "복사할 원본 세정코드를 입력하세요." });
        }

        var source = (await _products.ListAsync(p => p.CleaningCode == code, cancellationToken)).FirstOrDefault()
            ?? throw new ValidationException(new[] { $"세정코드 '{code}'에 해당하는 제품을 찾을 수 없습니다." });

        if (source.Id == targetProductId)
        {
            throw new ValidationException(new[] { "원본과 대상 세정코드가 같습니다." });
        }

        var sourceParams = await _parameters.ListAsync(p => p.ProductId == source.Id, cancellationToken);
        if (sourceParams.Count == 0)
        {
            throw new ValidationException(new[] { $"세정코드 '{code}'에 등록된 파라미터가 없습니다." });
        }

        var existing = await _parameters.ListAsync(p => p.ProductId == targetProductId, cancellationToken);
        var existingKeys = existing.Select(p => $"{p.Code}{p.Oper}").ToHashSet();
        var maxSort = (await _parameters.ListAllAsync(cancellationToken)).Select(p => (int?)p.SortOrder).Max() ?? 0;

        var copied = 0;
        foreach (var src in sourceParams.OrderBy(p => p.SortOrder))
        {
            if (!existingKeys.Add($"{src.Code}{src.Oper}"))
            {
                continue; // 대상에 이미 있는 (Code, Oper)는 건너뛴다.
            }
            await _parameters.AddAsync(new ParameterDefinition
            {
                Code = src.Code,
                Description = src.Description,
                ParameterType = src.ParameterType,
                Oper = src.Oper,
                ValueCount = src.ValueCount,
                MinValue = src.MinValue,
                MaxValue = src.MaxValue,
                Unit = src.Unit,
                IsActive = src.IsActive,
                ProductId = targetProductId,
                CertificateLabel = src.CertificateLabel,
                SortOrder = ++maxSort
            }, cancellationToken);
            copied++;
        }

        _auditLogger.Log("Parameter.Copy", nameof(ParameterDefinition), $"{code}->{targetProductId}:{copied}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return copied;
    }

    private static void ValidateParameter(ParameterUpsertRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add("PARAMETER(코드)를 입력하세요.");
        }
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors.Add("DESC(설명)를 입력하세요.");
        }
        if (request.ValueCount is < 0 or > 5)
        {
            errors.Add("Val.C(값 개수)는 0~5 사이여야 합니다.");
        }
        if (request.MinValue is { } min && request.MaxValue is { } max && min > max)
        {
            errors.Add("MIN이 MAX보다 클 수 없습니다.");
        }
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    // 레시피가 의미 있는 공정 = TRAN CODE.Start 행이 있는 공정(세정/건조/Laser&CO2/Bake) - 하드코딩된
    // OperCode 목록이 아니라 TranDefinitionService와 같은 방식으로 Master Data에서 유도한다.
    public async Task<IReadOnlyList<ProductRecipeSlotDto>> GetProductRecipeSlotsAsync(int productId, CancellationToken cancellationToken = default)
    {
        var startTransitions = await _transitions.ListAsync(t => t.TranCode == TranCode.Start && t.IsActive, cancellationToken);
        var recipeOperCodes = startTransitions.Select(t => t.SourceOperCode).Distinct().ToHashSet();

        var processes = await _processDefinitions.ListAsync(p => recipeOperCodes.Contains(p.OperCode), cancellationToken);
        var assignments = (await _productRecipes.ListAsync(a => a.ProductId == productId, cancellationToken))
            .ToDictionary(a => a.ProcessDefinitionId);
        var recipes = (await _recipes.ListAllAsync(cancellationToken)).ToDictionary(r => r.Id);

        return processes
            .OrderBy(p => p.OperCode)
            .Select(p =>
            {
                assignments.TryGetValue(p.Id, out var assignment);
                var recipe = assignment is not null && recipes.TryGetValue(assignment.RecipeDefinitionId, out var r) ? r : null;
                return new ProductRecipeSlotDto(p.Id, p.ProcessName, p.OperCode, recipe?.Id, recipe?.Code);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<int>> GetProductParameterIdsAsync(int productId, CancellationToken cancellationToken = default)
    {
        var assignments = await _productParameters.ListAsync(a => a.ProductId == productId, cancellationToken);
        return assignments.Select(a => a.ParameterDefinitionId).ToList();
    }

    public async Task<int?> GetProductDefaultLineIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");
        return product.DefaultLineId;
    }

    public async Task SetProductRecipeAsync(int productId, int processDefinitionId, int? recipeDefinitionId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var existing = (await _productRecipes.ListAsync(
            a => a.ProductId == productId && a.ProcessDefinitionId == processDefinitionId, cancellationToken)).FirstOrDefault();

        if (recipeDefinitionId is null)
        {
            if (existing is not null)
            {
                _productRecipes.Remove(existing);
            }
        }
        else if (existing is not null)
        {
            existing.RecipeDefinitionId = recipeDefinitionId.Value;
            _productRecipes.Update(existing);
        }
        else
        {
            await _productRecipes.AddAsync(new ProductRecipeAssignment
            {
                ProductId = productId,
                ProcessDefinitionId = processDefinitionId,
                RecipeDefinitionId = recipeDefinitionId.Value
            }, cancellationToken);
        }

        _auditLogger.Log("Product.SetRecipe", nameof(ProductRecipeAssignment), $"{productId}/{processDefinitionId}", _currentUser.GetCurrentUser(),
            $"RecipeDefinitionId={recipeDefinitionId}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ===== 제품별 공정별 레시피 다중 설정(2026-08-26) =====
    public async Task<IReadOnlyList<ProductRecipeDto>> GetProductRecipesAsync(int productId, CancellationToken cancellationToken = default)
    {
        var assignments = await _productRecipes.ListAsync(a => a.ProductId == productId, cancellationToken);
        var recipes = (await _recipes.ListAllAsync(cancellationToken)).ToDictionary(r => r.Id);
        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        return assignments
            .Select(a =>
            {
                recipes.TryGetValue(a.RecipeDefinitionId, out var r);
                processes.TryGetValue(a.ProcessDefinitionId, out var p);
                return new ProductRecipeDto(
                    a.Id, a.RecipeDefinitionId, r?.Code ?? "", r?.Description ?? "",
                    a.ProcessDefinitionId, p?.OperCode ?? 0, a.MinValue, a.MaxValue,
                    a.IsMain, a.IsActive, r?.ReadTimeMinutes);
            })
            .OrderBy(d => d.OperCode).ThenByDescending(d => d.IsMain).ThenBy(d => d.RecipeCode)
            .ToList();
    }

    public async Task<IReadOnlyList<ProductRecipeDto>> GetProductRecipesForOperAsync(int productId, int operCode, CancellationToken cancellationToken = default)
    {
        var all = await GetProductRecipesAsync(productId, cancellationToken);
        return all.Where(r => r.OperCode == operCode && r.IsActive)
            .OrderByDescending(r => r.IsMain).ThenBy(r => r.RecipeCode)
            .ToList();
    }

    public async Task<int> CreateProductRecipeAsync(ProductRecipeUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var recipe = await _recipes.GetByIdAsync(request.RecipeDefinitionId, cancellationToken)
            ?? throw new ValidationException(new[] { "레시피를 선택하세요." });
        var process = (await _processDefinitions.ListAsync(p => p.OperCode == recipe.OperCode, cancellationToken)).FirstOrDefault()
            ?? throw new ValidationException(new[] { $"레시피의 공정(OPER {recipe.OperCode})을 찾을 수 없습니다." });

        if (await _productRecipes.ExistsAsync(a => a.ProductId == request.ProductId && a.ProcessDefinitionId == process.Id && a.RecipeDefinitionId == request.RecipeDefinitionId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 레시피입니다: {recipe.Code}" });
        }
        ValidateRecipeSpec(request);

        if (request.IsMain)
        {
            await ClearMainAsync(request.ProductId, process.Id, cancellationToken);
        }

        var entity = new ProductRecipeAssignment
        {
            ProductId = request.ProductId,
            ProcessDefinitionId = process.Id,
            RecipeDefinitionId = request.RecipeDefinitionId,
            IsMain = request.IsMain,
            IsActive = request.IsActive,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue
        };
        await _productRecipes.AddAsync(entity, cancellationToken);
        _auditLogger.Log("Product.Recipe.Create", nameof(ProductRecipeAssignment), $"{request.ProductId}/{recipe.Code}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateProductRecipeAsync(int assignmentId, ProductRecipeUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var entity = await _productRecipes.GetByIdAsync(assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("레시피 설정을 찾을 수 없습니다.");
        ValidateRecipeSpec(request);

        if (request.IsMain && !entity.IsMain)
        {
            await ClearMainAsync(entity.ProductId, entity.ProcessDefinitionId, cancellationToken);
        }
        entity.IsMain = request.IsMain;
        entity.IsActive = request.IsActive;
        entity.MinValue = request.MinValue;
        entity.MaxValue = request.MaxValue;
        _productRecipes.Update(entity);
        _auditLogger.Log("Product.Recipe.Update", nameof(ProductRecipeAssignment), $"{entity.ProductId}/{assignmentId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProductRecipeAsync(int assignmentId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var entity = await _productRecipes.GetByIdAsync(assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("레시피 설정을 찾을 수 없습니다.");
        _productRecipes.Remove(entity);
        _auditLogger.Log("Product.Recipe.Delete", nameof(ProductRecipeAssignment), $"{entity.ProductId}/{assignmentId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearMainAsync(int productId, int processDefinitionId, CancellationToken cancellationToken)
    {
        var mains = await _productRecipes.ListAsync(a => a.ProductId == productId && a.ProcessDefinitionId == processDefinitionId && a.IsMain, cancellationToken);
        foreach (var m in mains) { m.IsMain = false; _productRecipes.Update(m); }
    }

    private static void ValidateRecipeSpec(ProductRecipeUpsertRequest request)
    {
        if (request.MinValue is { } mn && request.MaxValue is { } mx && mn > mx)
        {
            throw new ValidationException(new[] { "MIN 값이 MAX 값보다 클 수 없습니다." });
        }
    }

    public async Task<int> CopyProductRecipesAsync(int targetProductId, string sourceCleaningCode, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var code = (sourceCleaningCode ?? string.Empty).Trim();
        if (code.Length == 0) { throw new ValidationException(new[] { "복사할 원본 세정코드를 입력하세요." }); }
        var source = (await _products.ListAsync(p => p.CleaningCode == code, cancellationToken)).FirstOrDefault()
            ?? throw new ValidationException(new[] { $"세정코드 '{code}'에 해당하는 제품을 찾을 수 없습니다." });
        if (source.Id == targetProductId) { throw new ValidationException(new[] { "원본과 대상 세정코드가 같습니다." }); }

        var sourceRecipes = await _productRecipes.ListAsync(a => a.ProductId == source.Id, cancellationToken);
        if (sourceRecipes.Count == 0) { throw new ValidationException(new[] { $"세정코드 '{code}'에 등록된 레시피가 없습니다." }); }

        var existing = await _productRecipes.ListAsync(a => a.ProductId == targetProductId, cancellationToken);
        var existKeys = existing.Select(a => $"{a.ProcessDefinitionId}:{a.RecipeDefinitionId}").ToHashSet();
        var existMainProcesses = existing.Where(a => a.IsMain).Select(a => a.ProcessDefinitionId).ToHashSet();
        var copied = 0;
        foreach (var src in sourceRecipes)
        {
            if (!existKeys.Add($"{src.ProcessDefinitionId}:{src.RecipeDefinitionId}")) { continue; }
            // 대상에 해당 공정 MAIN이 이미 있으면 복사본은 MAIN 해제(공정당 MAIN 하나 유지).
            var asMain = src.IsMain && existMainProcesses.Add(src.ProcessDefinitionId);
            await _productRecipes.AddAsync(new ProductRecipeAssignment
            {
                ProductId = targetProductId,
                ProcessDefinitionId = src.ProcessDefinitionId,
                RecipeDefinitionId = src.RecipeDefinitionId,
                IsMain = asMain,
                IsActive = src.IsActive,
                MinValue = src.MinValue,
                MaxValue = src.MaxValue
            }, cancellationToken);
            copied++;
        }
        _auditLogger.Log("Product.Recipe.Copy", nameof(ProductRecipeAssignment), $"{code}->{targetProductId}:{copied}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return copied;
    }

    public async Task ToggleProductParameterAsync(int productId, int parameterDefinitionId, bool isAssigned, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var existing = (await _productParameters.ListAsync(
            a => a.ProductId == productId && a.ParameterDefinitionId == parameterDefinitionId, cancellationToken)).FirstOrDefault();

        if (isAssigned && existing is null)
        {
            await _productParameters.AddAsync(new ProductParameterAssignment
            {
                ProductId = productId,
                ParameterDefinitionId = parameterDefinitionId
            }, cancellationToken);
        }
        else if (!isAssigned && existing is not null)
        {
            _productParameters.Remove(existing);
        }
        else
        {
            return;
        }

        _auditLogger.Log("Product.ToggleParameter", nameof(ProductParameterAssignment), $"{productId}/{parameterDefinitionId}", _currentUser.GetCurrentUser(),
            $"IsAssigned={isAssigned}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetProductDefaultLineAsync(int productId, int? lineId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");

        product.DefaultLineId = lineId;
        _products.Update(product);

        _auditLogger.Log("Product.SetDefaultLine", nameof(Product), product.ProductCode, _currentUser.GetCurrentUser(), $"LineId={lineId}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
