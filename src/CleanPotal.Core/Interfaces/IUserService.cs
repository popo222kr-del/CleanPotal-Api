using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(bool includeResigned);
    Task<UserDto?> GetAsync(int id);
    Task<UserDto> CreateAsync(UserUpsertRequest req, string byUser);
    Task<UserDto?> UpdateAsync(int id, UserUpsertRequest req, string byUser);
    Task<bool> DeleteAsync(int id, string byUser);
    /// <summary>권한 매트릭스 일괄 변경 (변경 건수 반환).</summary>
    Task<int> BulkPermAsync(IReadOnlyList<UserPermChange> changes, string byUser);
    /// <summary>팀 단위 일괄 변경 (팀명/부서). 변경 인원수 반환.</summary>
    Task<int> TeamBulkAsync(TeamBulkRequest req, string byUser);
    Task<IReadOnlyList<UserAuditDto>> GetAuditAsync();
}
