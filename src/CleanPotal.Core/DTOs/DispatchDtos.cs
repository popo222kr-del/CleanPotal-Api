namespace CleanPotal.Core.DTOs;

public record DispatchDto(
    int Id, string VendorName, string OutgoingDetails, string IncomingDetails,
    string ManagerName, string ContactNumber, string FullAddress, string Note, DateTime CreateDate);

public record DispatchUpsertRequest(
    string VendorName, string OutgoingDetails, string IncomingDetails,
    string ManagerName, string ContactNumber, string FullAddress, string Note);
