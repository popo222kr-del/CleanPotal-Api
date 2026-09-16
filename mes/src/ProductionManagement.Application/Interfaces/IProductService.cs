using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 제품 마스터 관리. "셋업 > 제품 셋업" 화면이 쓴다.
// 제품도 지우지 않고 SetActive로 중지만 한다(과거 LOT이 참조한다).
public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(ProductUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateAsync(int productId, ProductUpsertRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default);
}
