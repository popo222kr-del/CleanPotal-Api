namespace ProductionManagement.Application.Exceptions;

// 두 사용자가 같은 Lot를 동시에 처리하려 할 때 발생 (CLAUDE.md 8번: 동시성은 UI Button Disable만으로
// 해결하지 않고 DB Transaction/Concurrency 검증으로 차단). Infrastructure의 UnitOfWork가
// DbUpdateConcurrencyException을 이 예외로 변환해서 던진다.
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message)
    {
    }
}
