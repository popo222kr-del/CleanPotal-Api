using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 제품 단가 관리 구현. 단가는 덮어쓰지 않고 적용일과 함께 이력으로 쌓아, 과거 시점의 단가를
// 그대로 되짚을 수 있게 한다. "셋업 > 단가/이미지" 탭이 쓴다.
public class ProductPriceService : IProductPriceService
{
    private readonly IRepository<ProductPrice, int> _prices;
    private readonly IRepository<Product, int> _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;

    public ProductPriceService(
        IRepository<ProductPrice, int> prices,
        IRepository<Product, int> products,
        IUnitOfWork unitOfWork,
        IAuthorizationService authorization,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser)
    {
        _prices = prices;
        _products = products;
        _unitOfWork = unitOfWork;
        _authorization = authorization;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ProductPriceDto>> GetPricesAsync(int productId, CancellationToken cancellationToken = default)
    {
        var list = await _prices.ListAsync(p => p.ProductId == productId, cancellationToken);
        return list.OrderBy(p => p.EffectiveDate).Select(p => new ProductPriceDto(p.Id, p.EffectiveDate, p.UnitPrice)).ToList();
    }

    public async Task<int> AddPriceAsync(int productId, DateTime effectiveDate, decimal unitPrice, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        if (unitPrice < 0)
        {
            throw new ValidationException(new[] { "단가는 0 이상이어야 합니다." });
        }
        var entity = new ProductPrice { ProductId = productId, EffectiveDate = effectiveDate.Date, UnitPrice = unitPrice };
        await _prices.AddAsync(entity, cancellationToken);
        _auditLogger.Log("Product.Price.Add", nameof(ProductPrice), $"{productId}/{effectiveDate:yyyy-MM-dd}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task DeletePriceAsync(int priceId, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var entity = await _prices.GetByIdAsync(priceId, cancellationToken)
            ?? throw new InvalidOperationException("단가 이력을 찾을 수 없습니다.");
        _prices.Remove(entity);
        _auditLogger.Log("Product.Price.Delete", nameof(ProductPrice), $"{priceId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]?> GetImageAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(productId, cancellationToken);
        return product?.ImageData;
    }

    public async Task SetImageAsync(int productId, byte[]? imageData, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");
        product.ImageData = imageData;
        _products.Update(product);
        _auditLogger.Log("Product.Image.Set", nameof(Product), $"{productId}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<(byte[]? Data, string? FileName)> GetTemplateAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(productId, cancellationToken);
        return (product?.CertificateTemplateData, product?.CertificateTemplateFileName);
    }

    public async Task SetTemplateAsync(int productId, byte[]? templateData, string? fileName, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminProduct, cancellationToken);
        var product = await _products.GetByIdAsync(productId, cancellationToken)
            ?? throw new InvalidOperationException("제품을 찾을 수 없습니다.");
        product.CertificateTemplateData = templateData;
        product.CertificateTemplateFileName = fileName;
        _products.Update(product);
        _auditLogger.Log("Product.CertTemplate.Set", nameof(Product), $"{productId}/{fileName}", _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
