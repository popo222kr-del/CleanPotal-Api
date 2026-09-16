using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 세정코드(제품)별 단가 이력 + 이미지 관리(2026-08-26 - "제품 단가/이미지" 탭).
public interface IProductPriceService
{
    Task<IReadOnlyList<ProductPriceDto>> GetPricesAsync(int productId, CancellationToken cancellationToken = default);
    Task<int> AddPriceAsync(int productId, DateTime effectiveDate, decimal unitPrice, CancellationToken cancellationToken = default);
    Task DeletePriceAsync(int priceId, CancellationToken cancellationToken = default);

    Task<byte[]?> GetImageAsync(int productId, CancellationToken cancellationToken = default);
    Task SetImageAsync(int productId, byte[]? imageData, CancellationToken cancellationToken = default);

    // 2026-08-27: 세정코드별 성적서 기본 양식(Excel). 파일명은 화면 표시/생성 시 확장자 판단용.
    Task<(byte[]? Data, string? FileName)> GetTemplateAsync(int productId, CancellationToken cancellationToken = default);
    Task SetTemplateAsync(int productId, byte[]? templateData, string? fileName, CancellationToken cancellationToken = default);
}
