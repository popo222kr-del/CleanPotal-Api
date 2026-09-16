using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 제품 셋업 "기준정보" 탭 전용: LINE(기본값 1개)/RECIPE(공정별 1개)/파라미터(다중선택) 조회 및 배정.
public interface IProductReferenceDataService
{
    Task<IReadOnlyList<LineOptionDto>> GetLinesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecipeOptionDto>> GetRecipesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParameterOptionDto>> GetParametersAsync(CancellationToken cancellationToken = default);

    // 파라미터 관리(2026-08-25 - 제품 셋업 파라미터 표를 하단 폼에서 직접 추가/수정/삭제).
    // GetAll은 비활성(미사용) 포함 전체를 반환한다(표에 USE=미사용으로 보여주기 위해).
    Task<IReadOnlyList<ParameterOptionDto>> GetAllParametersAsync(CancellationToken cancellationToken = default);
    // 2026-08-26: 파라미터는 제품별. 선택된 제품의 검사 항목만(미사용 포함) 반환한다.
    Task<IReadOnlyList<ParameterOptionDto>> GetProductParametersAsync(int productId, CancellationToken cancellationToken = default);
    Task<int> CreateParameterAsync(ParameterUpsertRequest request, CancellationToken cancellationToken = default);
    Task UpdateParameterAsync(int parameterId, ParameterUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteParameterAsync(int parameterId, CancellationToken cancellationToken = default);
    // 2026-08-26 COPY 탭: 원본 세정코드의 파라미터를 대상 제품으로 복사(중복 (Code,Oper)는 건너뜀). 복사 개수 반환.
    Task<int> CopyParametersAsync(int targetProductId, string sourceCleaningCode, CancellationToken cancellationToken = default);
    // 2026-08-26: 파라미터 편집 폼 PARAMETER 선택용 카탈로그(코드+설명), OPER 선택용 공정 목록.
    Task<IReadOnlyList<ParameterCatalogDto>> GetParameterCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperOptionDto>> GetOperOptionsAsync(CancellationToken cancellationToken = default);

    // 작업시작(Start) TRAN이 있는 4개 공정(세정/건조/Laser&CO2/Bake)마다 이 제품에 배정된 레시피 슬롯 하나씩.
    Task<IReadOnlyList<ProductRecipeSlotDto>> GetProductRecipeSlotsAsync(int productId, CancellationToken cancellationToken = default);

    // 2026-08-26: 제품별 "공정별 레시피" 다중 설정. 전체 조회/OPER별 조회(OPER 화면 RECIPE ID 드롭다운용)/CRUD.
    Task<IReadOnlyList<ProductRecipeDto>> GetProductRecipesAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductRecipeDto>> GetProductRecipesForOperAsync(int productId, int operCode, CancellationToken cancellationToken = default);
    Task<int> CreateProductRecipeAsync(ProductRecipeUpsertRequest request, CancellationToken cancellationToken = default);
    Task UpdateProductRecipeAsync(int assignmentId, ProductRecipeUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteProductRecipeAsync(int assignmentId, CancellationToken cancellationToken = default);
    Task<int> CopyProductRecipesAsync(int targetProductId, string sourceCleaningCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetProductParameterIdsAsync(int productId, CancellationToken cancellationToken = default);
    Task<int?> GetProductDefaultLineIdAsync(int productId, CancellationToken cancellationToken = default);

    Task SetProductRecipeAsync(int productId, int processDefinitionId, int? recipeDefinitionId, CancellationToken cancellationToken = default);
    Task ToggleProductParameterAsync(int productId, int parameterDefinitionId, bool isAssigned, CancellationToken cancellationToken = default);
    Task SetProductDefaultLineAsync(int productId, int? lineId, CancellationToken cancellationToken = default);
}
