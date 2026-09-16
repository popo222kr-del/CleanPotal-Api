using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// 관리자가 특정 Worker에게 개별로 켜준 권한 1건. Admin Role은 이 테이블을 보지 않고 전부
// 허용되므로 보통 Admin 계정에는 행을 만들지 않는다(AuthorizationService 참고).
public class UserPermission : Entity<int>
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public PermissionCode PermissionCode { get; set; }
    public string GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
}
