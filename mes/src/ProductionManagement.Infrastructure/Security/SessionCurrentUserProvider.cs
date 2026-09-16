using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Infrastructure.Security;

// 로그인 이후의 세션 사용자를 보관한다(Singleton). 기존 WindowsCurrentUserProvider를 대체한다.
// GetCurrentUser()는 감사 로그 작성자·권한 조회에 쓰이는 식별자(LoginId)를 돌려준다.
public class SessionCurrentUserProvider : ICurrentUserProvider
{
    public AuthUser? Current { get; set; }

    public void SignIn(AuthUser user) => Current = user;
    public void SignOut() => Current = null;

    // 로그인 전(시딩/마이그레이션 등)에는 "SYSTEM"으로 기록한다.
    public string GetCurrentUser() => Current?.LoginId ?? "SYSTEM";
}
