using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Validators;

// 입력값 검증. 서비스의 Create/Update 첫머리에서 불러 오류 목록을 받고, 하나라도 있으면
// ValidationException으로 던져 화면에 그대로 보여준다. 오류를 모아서 돌려주므로 사용자는
// 잘못된 항목을 한 번에 확인할 수 있다(하나 고치면 다음 오류가 나오는 식이 아니다).
// 대상: 제품 등록/수정 - 세정코드(유일 키)·제품 규격·품목코드·제품명·업체 선택.
public static class ProductValidator
{
    public static IReadOnlyList<string> Validate(ProductUpsertRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CleaningCode))
        {
            errors.Add("세정코드를 입력하세요.");
        }
        else if (request.CleaningCode.Length > 50)
        {
            errors.Add("세정코드는 50자를 넘을 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductCode))
        {
            errors.Add("제품 규격을 입력하세요.");
        }
        else if (request.ProductCode.Length > 50)
        {
            errors.Add("제품 규격은 50자를 넘을 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(request.ItemCode))
        {
            errors.Add("품목코드를 입력하세요.");
        }
        else if (request.ItemCode.Length > 50)
        {
            errors.Add("품목코드는 50자를 넘을 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            errors.Add("제품명을 입력하세요.");
        }
        else if (request.ProductName.Length > 100)
        {
            errors.Add("제품명은 100자를 넘을 수 없습니다.");
        }

        if (request.SerialNumber is { Length: > 100 })
        {
            errors.Add("제품 단가는 100자를 넘을 수 없습니다.");
        }

        if (request.CustomerId <= 0)
        {
            errors.Add("업체를 선택하세요.");
        }

        return errors;
    }
}
