using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 매출 고객사(업체) 마스터 관리. "셋업 > 업체 관리" 화면이 쓴다.
// 업체는 지우지 않고 SetActive로 중지만 한다 - 과거 LOT이 참조하고 있기 때문이다.
public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CustomerDto> CreateAsync(CustomerUpsertRequest request, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateAsync(int customerId, CustomerUpsertRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken = default);
}
