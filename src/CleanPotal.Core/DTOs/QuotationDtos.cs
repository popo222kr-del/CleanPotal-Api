namespace CleanPotal.Core.DTOs;

public record QuotationItemDto(
    int Id, int SortOrder, string ItemName, string Spec, string Unit,
    decimal Quantity, decimal UnitPrice, decimal Amount, string Remarks);

public record QuotationDto(
    int Id, string QuoteNo, string VendorName, DateOnly? QuoteDate, DateOnly? ValidUntil,
    string Status, string Remarks, string CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt,
    decimal Total, IReadOnlyList<QuotationItemDto> Items);

/// <summary>목록용 요약 (품목 제외).</summary>
public record QuotationSummaryDto(
    int Id, string QuoteNo, string VendorName, DateOnly? QuoteDate, DateOnly? ValidUntil,
    string Status, decimal Total, int ItemCount, string CreatedBy);

public record QuotationItemRequest(
    string ItemName, string Spec, string Unit, decimal Quantity, decimal UnitPrice, string Remarks);

public record QuotationUpsertRequest(
    string QuoteNo, string VendorName, DateOnly? QuoteDate, DateOnly? ValidUntil,
    string Status, string Remarks, IReadOnlyList<QuotationItemRequest> Items);
