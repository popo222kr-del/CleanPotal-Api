namespace ProductionManagement.Domain.Entities;

// 2026-08-26: 세정코드(제품)별 단가 이력. 적용일자마다 단가를 남겨 시점별 단가를 관리한다(3번째 참조
// 이미지의 "제품 단가 및 이미지 등록" - 적용일자/단가 그리드).
public class ProductPrice : Entity<int>
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public DateTime EffectiveDate { get; set; }
    public decimal UnitPrice { get; set; }
}
