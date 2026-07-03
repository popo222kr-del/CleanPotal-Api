namespace CleanPotal.Core.DTOs;

public record QuotationItemDto(
    int Id, int No, string Description, string PartCode, string StandardSpec,
    decimal ListPrice, decimal Qty, decimal Amount);

public record QuotationDto(
    int Id, string QuoteNo, string RfqNo, string Company, string Attention,
    string Email, string Phone, DateOnly? QuoteDate, string Validity,
    string AetsManager, string AetsPhone, string BusinessNo,
    string Remarks, string Memo, string SourceFileName,
    string CreatedBy, DateTime CreatedAt, string LastModifiedBy, DateTime? LastModifiedAt,
    decimal Total, IReadOnlyList<QuotationItemDto> Items);

/// <summary>목록용 요약 (품목 제외).</summary>
public record QuotationSummaryDto(
    int Id, string QuoteNo, string RfqNo, string Company, DateOnly? QuoteDate, string Validity,
    decimal Total, int ItemCount, string AetsManager, DateTime CreatedAt);

public record QuotationItemRequest(
    int No, string Description, string PartCode, string StandardSpec, decimal ListPrice, decimal Qty);

public record QuotationUpsertRequest(
    string QuoteNo, string RfqNo, string Company, string Attention, string Email, string Phone,
    DateOnly? QuoteDate, string Validity, string AetsManager, string AetsPhone, string BusinessNo,
    string Remarks, string Memo, IReadOnlyList<QuotationItemRequest> Items);
