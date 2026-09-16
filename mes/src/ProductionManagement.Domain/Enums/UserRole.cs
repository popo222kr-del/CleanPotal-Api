namespace ProductionManagement.Domain.Enums;

// 계정 등급. 실제 화면 접근 권한은 이 값이 아니라 UserPermission(PermissionCode 단위)이 정한다.
// Admin은 그 권한 검사를 통과하는 특별 취급을 받는다(AuthorizationService 참고) - 즉 이 enum은
// "권한 목록을 일일이 보지 않아도 되는 관리자인가"를 가르는 스위치에 가깝다.
public enum UserRole
{
    Worker = 0,
    Admin = 1
}
