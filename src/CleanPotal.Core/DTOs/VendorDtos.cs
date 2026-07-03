namespace CleanPotal.Core.DTOs;

public record VendorDto(
    int Id, string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string BasePath, string Addresses, string Managers,
    string Contact, string Phone, string Note);

public record VendorUpsertRequest(
    string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string? BasePath, string? Addresses, string? Managers,
    string? Contact, string? Phone, string? Note);
