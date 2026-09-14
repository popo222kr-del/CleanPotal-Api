namespace CleanPotal.Core;

/// <summary>
/// 업무 규칙 위반 / 잘못된 입력 — 서버 버그가 아니라 사용자가 고칠 수 있는 문제.
/// ExceptionMiddleware 가 400 Bad Request 로 변환하고 Message 를 그대로 사용자에게 보여준다.
///
/// 프레임워크·EF 가 던지는 InvalidOperationException(진짜 버그) 과 섞이면
/// 내부 오류 메시지가 사용자에게 새어 나가거나 실제 버그가 400으로 가려지므로,
/// "사용자에게 보여줘도 되는 메시지"는 반드시 이 타입으로 던진다.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
