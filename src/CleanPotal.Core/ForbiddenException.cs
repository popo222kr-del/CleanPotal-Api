namespace CleanPotal.Core;

/// <summary>
/// 로그인은 되어 있으나 이 자료에 대해 권한이 없음 → 403 Forbidden.
///
/// 영역 등급 검사(<c>DbPermissionHandler</c>)는 통과했지만 "이 글의 작성자가 아니라서"
/// 처럼 **자료 단위**로 막아야 할 때 쓴다. 등급 자체가 모자라면 정책이 먼저 403 을 낸다.
/// 메시지는 사용자에게 그대로 보여준다.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
