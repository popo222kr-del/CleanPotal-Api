namespace CleanPotal.Core.DTOs;

public record VendorDto(int Id, string VendorName, string Category, bool IsWeekly, string Contact, string Phone, string Note);

public record VendorUpsertRequest(string VendorName, string Category, bool IsWeekly, string Contact, string Phone, string Note);
