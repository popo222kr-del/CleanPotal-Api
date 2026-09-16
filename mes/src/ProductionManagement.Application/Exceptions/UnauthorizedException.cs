namespace ProductionManagement.Application.Exceptions;

// 권한이 없는 사용자가 관리 기능(Rollback, 관리자 화면 각 탭 등)을 호출했을 때 Service Layer가
// 던진다. ValidationException("입력이 잘못됨")과 달리 "이 사용자는 이 작업을 할 수 없음"을 나타낸다.
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
