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
    Task<int> DeptBulkAsync(string oldDept, string newDept, string byUser);
    Task<OrgTreeDto> GetOrgAsync();
    Task<string?> AddOrgAsync(string kind, string name, string? parent, string byUser);

    /// <summary>부서를 본부(사업본부)에 연결. division 을 비우면 '본부 미지정'.</summary>
    Task<string?> SetDeptDivisionAsync(string dept, string division, string byUser);

    /// <summary>본부 이름 변경. 그 본부를 가리키는 부서들도 함께 따라간다.</summary>
    Task<string?> RenameDivisionAsync(string oldName, string newName, string byUser);
    Task<string?> DeleteOrgAsync(string kind, string name, string? parent, string byUser);

    /// <summary>팀의 교대 조를 지정한다(0/1/2). 실패 사유 문자열, 성공이면 null.</summary>
    Task<string?> SetOrgShiftGroupAsync(string name, int shiftGroup, string byUser, string? parent = null);

    /// <summary>팀의 생산팀 여부(근무표 표시·생산직 집계). 교대조와 별개 축이다.</summary>
    Task<string?> SetOrgProductionAsync(string name, bool isProduction, string byUser, string? parent = null);

    /// <summary>대시보드 근무 현황 · 일정 달력에 띄울지. 보내지 않은 값(null)은 그대로 둔다.</summary>
    Task<string?> SetOrgVisibilityAsync(string kind, string name, string? parent,
                                        bool? showOnDashboard, bool? showOnCalendar, string byUser);

    /// <summary>이 팀이 WPF 에서 쓰던 이름들(쉼표 구분)을 기록한다.</summary>
    Task<string?> SetOrgLegacyNamesAsync(string name, string legacyNames, string byUser, string? parent = null);

    /// <summary>부서의 달력 표시 설정(색·약칭). 비우면 자동값으로 되돌린다.</summary>
    Task<string?> SetDeptStyleAsync(string name, string? color, string? shortName, string byUser);
    Task<IReadOnlyList<UserAuditDto>> GetAuditAsync();
}
