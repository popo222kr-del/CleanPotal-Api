using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Validators;

// 입력값 검증. 서비스의 Create/Update 첫머리에서 불러 오류 목록을 받고, 하나라도 있으면
// ValidationException으로 던져 화면에 그대로 보여준다. 오류를 모아서 돌려주므로 사용자는
// 잘못된 항목을 한 번에 확인할 수 있다(하나 고치면 다음 오류가 나오는 식이 아니다).
// 대상: 공정 플로우 등록/수정 - 코드·이름과 함께, 공정 순서가 최소 1개는 있어야 한다.
public static class ProcessRouteValidator
{
    public static IReadOnlyList<string> Validate(ProcessRouteUpsertRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RouteCode))
        {
            errors.Add("플로우 코드를 입력하세요.");
        }
        else if (request.RouteCode.Length > 30)
        {
            errors.Add("플로우 코드는 30자를 넘을 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(request.RouteName))
        {
            errors.Add("플로우 이름을 입력하세요.");
        }
        else if (request.RouteName.Length > 50)
        {
            errors.Add("플로우 이름은 50자를 넘을 수 없습니다.");
        }

        if (request.ProcessDefinitionIds.Count == 0)
        {
            errors.Add("공정 순서를 1개 이상 추가하세요.");
        }

        return errors;
    }
}
