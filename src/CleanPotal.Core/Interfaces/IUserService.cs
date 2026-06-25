using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(bool includeResigned);
    Task<UserDto?> GetAsync(int id);
    Task<UserDto> CreateAsync(UserUpsertRequest req);
    Task<UserDto?> UpdateAsync(int id, UserUpsertRequest req);
    Task<bool> DeleteAsync(int id);
}
