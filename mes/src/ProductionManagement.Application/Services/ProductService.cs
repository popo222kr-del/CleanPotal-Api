using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Validators;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 제품 마스터 관리 구현. 유일 키는 세정코드이며 중복 등록을 막는다.
// 삭제 대신 활성/중지만 전환한다 - 과거 LOT이 제품을 참조하기 때문이다.
public class ProductService : IProductService
{
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<Customer, int> _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public ProductService(
        IRepository<Product, int> products,
        IRepository<Customer, int> customers,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _products = products;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var products = await _products.ListAllAsync(cancellationToken);
        var customers = (await _customers.ListAllAsync(cancellationToken)).ToDictionary(c => c.Id);

        return products
            .OrderBy(p => p.CleaningCode)
            .Select(p => ToDto(p, customers.TryGetValue(p.CustomerId, out var c) ? c.CustomerName : "-"))
            .ToList();
    }

    public async Task<ProductDto> CreateAsync(ProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var errors = ProductValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var customer = await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new ValidationException(new[] { "선택한 업체를 찾을 수 없습니다." });

        if (await _products.ExistsAsync(p => p.CleaningCode == request.CleaningCode, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 세정코드입니다: {request.CleaningCode}" });
        }

        var now = DateTime.Now;
        var product = new Product
        {
            ProductCode = request.ProductCode,
            ItemCode = request.ItemCode,
            ProductName = request.ProductName,
            SerialNumber = request.SerialNumber,
            CleaningCode = request.CleaningCode,
            ItemCategory = request.ItemCategory,
            CustomerId = request.CustomerId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _products.AddAsync(product, cancellationToken);
        // 감사로그 식별자는 CleaningCode를 쓴다 - ProductCode는 이제 "제품 규격"이라 유일하지 않다.
        _auditLogger.Log("Product.Create", nameof(Product), product.CleaningCode, _currentUser.GetCurrentUser(),
            $"ProductName={product.ProductName}, CustomerId={product.CustomerId}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(product, customer.CustomerName);
    }

    public async Task<ProductDto> UpdateAsync(int productId, ProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var errors = ProductValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");

        var customer = await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new ValidationException(new[] { "선택한 업체를 찾을 수 없습니다." });

        if (await _products.ExistsAsync(p => p.CleaningCode == request.CleaningCode && p.Id != productId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 등록된 세정코드입니다: {request.CleaningCode}" });
        }

        product.ProductCode = request.ProductCode;
        product.ItemCode = request.ItemCode;
        product.ProductName = request.ProductName;
        product.SerialNumber = request.SerialNumber;
        product.CleaningCode = request.CleaningCode;
        product.ItemCategory = request.ItemCategory;
        product.CustomerId = request.CustomerId;
        product.UpdatedAt = DateTime.Now;

        _products.Update(product);
        _auditLogger.Log("Product.Update", nameof(Product), product.CleaningCode, _currentUser.GetCurrentUser(),
            $"ProductName={product.ProductName}, CustomerId={product.CustomerId}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(product, customer.CustomerName);
    }

    public async Task SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);

        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");

        product.IsActive = isActive;
        product.UpdatedAt = DateTime.Now;

        _products.Update(product);
        _auditLogger.Log(isActive ? "Product.Activate" : "Product.Deactivate", nameof(Product), product.CleaningCode, _currentUser.GetCurrentUser());

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static ProductDto ToDto(Product product, string customerName) => new(
        product.Id,
        product.ProductCode,
        product.ItemCode,
        product.ProductName,
        product.SerialNumber,
        product.CleaningCode,
        product.CustomerId,
        customerName,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt,
        product.ItemCategory);
}
