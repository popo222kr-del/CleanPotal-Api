using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Validators;

// 입력값 검증. 서비스의 Create/Update 첫머리에서 불러 오류 목록을 받고, 하나라도 있으면
// ValidationException으로 던져 화면에 그대로 보여준다. 오류를 모아서 돌려주므로 사용자는
// 잘못된 항목을 한 번에 확인할 수 있다(하나 고치면 다음 오류가 나오는 식이 아니다).
// 대상: 전산등록 - 업체/품목/공정 경로 선택과 LINE, 그리고 LOT코드 2자리 규칙.
public static class RegistrationValidator
{
    public static IReadOnlyList<string> Validate(RegistrationCreateRequest request)
    {
        var errors = new List<string>();

        if (request.CustomerId <= 0)
        {
            errors.Add("업체를 선택하세요.");
        }

        if (request.ProductId <= 0)
        {
            errors.Add("품목을 선택하세요.");
        }

        if (request.ProcessRouteId <= 0)
        {
            errors.Add("공정 경로를 선택하세요.");
        }

        if (string.IsNullOrWhiteSpace(request.Line))
        {
            errors.Add("LINE을 입력하세요.");
        }

        if (string.IsNullOrWhiteSpace(request.LotCode) || request.LotCode.Trim().Length != 2)
        {
            errors.Add("LOT코드는 2자리로 입력하세요.");
        }

        if (request.Quantity <= 0)
        {
            errors.Add("수량은 1 이상이어야 합니다.");
        }

        // 2026-09-03 피드백(#10): 과거 일자는 허용하되 미래 일자는 등록 불가(오늘까지만).
        if (request.ShipDate.Date > DateTime.Today)
        {
            errors.Add("고객출고일은 미래 일자로 등록할 수 없습니다(오늘까지만 가능).");
        }

        return errors;
    }
}
