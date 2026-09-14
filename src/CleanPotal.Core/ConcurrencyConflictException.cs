namespace CleanPotal.Core;

/// <summary>
/// 내가 화면에 띄워둔 사이에 다른 사람이 먼저 저장했음 → 409 Conflict.
///
/// 마지막에 저장한 사람이 남의 수정을 조용히 덮어쓰는 것을 막는다.
/// 사용자는 새로고침해서 상대의 변경을 확인한 뒤 다시 저장해야 한다.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message) { }
}
