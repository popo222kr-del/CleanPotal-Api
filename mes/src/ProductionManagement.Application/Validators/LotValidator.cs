using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Validators;

// 입력값 검증. 서비스의 Create/Update 첫머리에서 불러 오류 목록을 받고, 하나라도 있으면
// ValidationException으로 던져 화면에 그대로 보여준다. 오류를 모아서 돌려주므로 사용자는
// 잘못된 항목을 한 번에 확인할 수 있다(하나 고치면 다음 오류가 나오는 식이 아니다).
// 대상: LOT 생성 - 제품 선택, 수량 1 이상, 입고일 유효성, 특이사항 길이.
public static class LotValidator
{
    public static IReadOnlyList<string> Validate(LotCreateRequest request)
    {
        var errors = new List<string>();

        if (request.ProductId <= 0)
        {
            errors.Add("제품을 선택하세요.");
        }

        if (request.Quantity <= 0)
        {
            errors.Add("수량은 1 이상이어야 합니다.");
        }

        if (request.ReceivedDate > DateTime.Now.AddDays(1))
        {
            errors.Add("입고일이 올바르지 않습니다.");
        }

        if (request.Remarks is { Length: > 500 })
        {
            errors.Add("특이사항은 500자를 넘을 수 없습니다.");
        }

        return errors;
    }
}
