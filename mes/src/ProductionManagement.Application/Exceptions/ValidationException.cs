namespace ProductionManagement.Application.Exceptions;

// System.ComponentModel.DataAnnotations.ValidationException은 메시지 하나만 담을 수 있어서
// 여러 항목을 한 번에 검증하는 화면(Customer/Product 등록 폼)에 맞게 별도로 정의한다.
public class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }
}
