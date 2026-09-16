namespace ProductionManagement.Application.DTOs;

// ProductCode/SerialNumber 필드명은 예전 그대로지만, 화면 표시 이름은 각각 "제품 규격"/"제품 단가"다
// (2026-08-21 - Product.cs 주석 참고, DB 컬럼명 변경 없이 화면 라벨만 전환).
public record ProductDto(
    int ProductId,
    string ProductCode,
    string ItemCode,
    string ProductName,
    string? SerialNumber,
    string CleaningCode,
    int CustomerId,
    string CustomerName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ItemCategory = null);

public record ProductUpsertRequest(
    string CleaningCode,
    string ProductCode,
    string ItemCode,
    string ProductName,
    string? SerialNumber,
    int CustomerId,
    string? ItemCategory = null);
