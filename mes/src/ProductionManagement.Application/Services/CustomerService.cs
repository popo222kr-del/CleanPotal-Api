using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Validators;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 업체 마스터 관리 구현. 업체코드 중복을 막고, 삭제 대신 활성/중지만 전환한다
// - 과거 LOT과 제품이 업체를 참조하고 있어 지우면 이력이 끊긴다.
public class CustomerService : ICustomerService
{
    private readonly IRepository<Customer, int> _customers;
    private readonly IRepository<LineDefinition, int> _lines;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public CustomerService(
        IRepository<Customer, int> customers,
        IRepository<LineDefinition, int> lines,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _customers = customers;
        _lines = lines;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _customers.ListAllAsync(cancellationToken);
        var lines = (await _lines.ListAllAsync(cancellationToken)).ToDictionary(l => l.Id);
        return customers
            .OrderBy(c => c.CustomerCode)
            .Select(c => ToDto(c, lines.GetValueOrDefault(c.LineDefinitionId ?? -1)))
            .ToList();
    }

    public async Task<CustomerDto> CreateAsync(CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminCustomer, cancellationToken);

        var errors = CustomerValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (await _customers.ExistsAsync(c => c.CustomerCode == request.CustomerCode, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 업체 코드입니다: {request.CustomerCode}" });
        }

        if (await _customers.ExistsAsync(c => c.ExportPrefix == request.ExportPrefix, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 사용 중인 반출번호 약어입니다: {request.ExportPrefix}" });
        }

        var now = DateTime.Now;
        var customer = new Customer
        {
            CustomerCode = request.CustomerCode,
            CustomerName = request.CustomerName,
            ExportPrefix = request.ExportPrefix,
            LineDefinitionId = request.LineDefinitionId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _customers.AddAsync(customer, cancellationToken);
        _auditLogger.Log("Customer.Create", nameof(Customer), customer.CustomerCode, _currentUser.GetCurrentUser(),
            $"CustomerName={customer.CustomerName}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(customer, await ResolveLineAsync(customer.LineDefinitionId, cancellationToken));
    }

    public async Task<CustomerDto> UpdateAsync(int customerId, CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminCustomer, cancellationToken);

        var errors = CustomerValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("업체를 찾을 수 없습니다.");

        if (await _customers.ExistsAsync(c => c.CustomerCode == request.CustomerCode && c.Id != customerId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 업체 코드입니다: {request.CustomerCode}" });
        }

        if (await _customers.ExistsAsync(c => c.ExportPrefix == request.ExportPrefix && c.Id != customerId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 사용 중인 반출번호 약어입니다: {request.ExportPrefix}" });
        }

        customer.CustomerCode = request.CustomerCode;
        customer.CustomerName = request.CustomerName;
        customer.ExportPrefix = request.ExportPrefix;
        customer.LineDefinitionId = request.LineDefinitionId;
        customer.UpdatedAt = DateTime.Now;

        _customers.Update(customer);
        _auditLogger.Log("Customer.Update", nameof(Customer), customer.CustomerCode, _currentUser.GetCurrentUser(),
            $"CustomerName={customer.CustomerName}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(customer, await ResolveLineAsync(customer.LineDefinitionId, cancellationToken));
    }

    public async Task SetActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminCustomer, cancellationToken);

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("업체를 찾을 수 없습니다.");

        customer.IsActive = isActive;
        customer.UpdatedAt = DateTime.Now;

        _customers.Update(customer);
        _auditLogger.Log(isActive ? "Customer.Activate" : "Customer.Deactivate", nameof(Customer), customer.CustomerCode, _currentUser.GetCurrentUser());

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<LineDefinition?> ResolveLineAsync(int? lineDefinitionId, CancellationToken cancellationToken)
        => lineDefinitionId is { } id ? await _lines.GetByIdAsync(id, cancellationToken) : null;

    private static CustomerDto ToDto(Customer customer, LineDefinition? line) => new(
        customer.Id,
        customer.CustomerCode,
        customer.CustomerName,
        customer.ExportPrefix,
        customer.LineDefinitionId,
        line?.Code,
        line?.Description,
        customer.IsActive,
        customer.CreatedAt,
        customer.UpdatedAt);
}
