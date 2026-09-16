namespace ProductionManagement.Domain.Enums;

// 관리자가 개별 작업자에게 켜줄 수 있는 권한 항목. 고정된 6개뿐이라 Permission 마스터 테이블 대신
// enum으로 관리한다(권한 설계 스펙 "데이터 모델" 참고 - 항목이 늘어날 일이 당장 없는 닫힌 집합).
public enum PermissionCode
{
    Rollback = 0,
    AdminCustomer = 1,
    AdminProduct = 2,
    AdminProcess = 3,
    AdminCertificate = 4,
    AdminUserManagement = 5
}
