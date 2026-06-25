namespace CleanPotal.Core.DTOs;

public record PortalGroupDto(int Id, string Name, int SortOrder, IReadOnlyList<PortalItemDto> Items);

public record PortalItemDto(int Id, int GroupId, string Title, string Path, string Type, int SortOrder);

public record PortalGroupRequest(string Name);

public record PortalItemRequest(int GroupId, string Title, string Path, string? Type);
