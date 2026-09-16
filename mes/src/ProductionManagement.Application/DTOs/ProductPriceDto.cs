namespace ProductionManagement.Application.DTOs;

// 세정코드(제품)별 단가 이력 한 줄(적용일자/단가).
public record ProductPriceDto(int Id, DateTime EffectiveDate, decimal UnitPrice);
