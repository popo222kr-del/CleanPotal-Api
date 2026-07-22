namespace CleanPotal.Core.DTOs;

public record VendorDto(
    int Id, string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string BasePath, string LinkUrl, string Addresses, string Managers);

public record VendorUpsertRequest(
    string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string? BasePath, string? LinkUrl, string? Addresses, string? Managers);
