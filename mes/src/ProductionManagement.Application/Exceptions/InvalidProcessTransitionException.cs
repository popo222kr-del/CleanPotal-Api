namespace ProductionManagement.Application.Exceptions;

// 지금 상태에서 허용되지 않는 공정 전이를 시도했을 때 던진다.
// 예: 입고검사를 건너뛰고 세정으로 보내려 하거나, HOLD 중인 LOT을 그냥 진행시키려는 경우.
// 어떤 전이가 가능한지는 ProcessTransitionDefinition(TRAN 코드) 마스터가 정한다.
// 메시지는 사용자에게 그대로 보여줄 수 있는 문장으로 담는다.
public class InvalidProcessTransitionException : Exception
{
    public InvalidProcessTransitionException(string message) : base(message)
    {
    }
}
