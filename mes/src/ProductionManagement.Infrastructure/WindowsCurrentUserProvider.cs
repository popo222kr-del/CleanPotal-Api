using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Infrastructure;

// "지금 조작하는 사람"을 Windows 로그인 계정으로 답하는 구현. 감사 로그의 행위자 이름에 쓰인다.
// 2026-08-28에 앱 로그인이 아이디/비밀번호 방식으로 바뀌면서 실제 화면에서는 로그인한 계정을 쓰는
// 다른 구현이 우선한다 - 이 클래스는 로그인 세션이 없는 상황(콘솔/설계타임 등)의 대비책이다.
public class WindowsCurrentUserProvider : ICurrentUserProvider
{
    public string GetCurrentUser()
    {
        var domain = Environment.UserDomainName;
        var name = Environment.UserName;
        return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, name, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{domain}\\{name}";
    }
}
