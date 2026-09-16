using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Web.Security;

// 앱 시작 시(마이그레이션/시드/최초 관리자 생성)처럼 로그인 사용자가 아직 없는 컨텍스트에서 쓰는 provider.
// DevelopmentDataSeeder는 이 값을 실제로 쓰지 않지만(시그니처 호환용) 시작 스코프에서 안전하게 넘겨주기 위함.
public sealed class SystemCurrentUserProvider : ICurrentUserProvider
{
    public string GetCurrentUser() => "SYSTEM";
}
