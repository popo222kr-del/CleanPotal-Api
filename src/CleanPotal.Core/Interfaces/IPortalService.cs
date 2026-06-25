using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IPortalService
{
    Task<IReadOnlyList<PortalGroupDto>> GetAllAsync();

    Task<PortalGroupDto> AddGroupAsync(PortalGroupRequest req);
    Task<PortalGroupDto?> RenameGroupAsync(int id, PortalGroupRequest req);
    Task<bool> DeleteGroupAsync(int id);

    Task<PortalItemDto> AddItemAsync(PortalItemRequest req);
    Task<PortalItemDto?> UpdateItemAsync(int id, PortalItemRequest req);
    Task<bool> DeleteItemAsync(int id);
}
