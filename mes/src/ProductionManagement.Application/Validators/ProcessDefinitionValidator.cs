using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Validators;

// 입력값 검증. 서비스의 Create/Update 첫머리에서 불러 오류 목록을 받고, 하나라도 있으면
// ValidationException으로 던져 화면에 그대로 보여준다. 오류를 모아서 돌려주므로 사용자는
// 잘못된 항목을 한 번에 확인할 수 있다(하나 고치면 다음 오류가 나오는 식이 아니다).
// 대상: 공정 마스터 등록/수정 - 공정 코드·공정명의 필수 여부와 길이.
public static class ProcessDefinitionValidator
{
    public static IReadOnlyList<string> Validate(ProcessDefinitionUpsertRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ProcessCode))
        {
            errors.Add("공정 코드를 입력하세요.");
        }
        else if (request.ProcessCode.Length > 30)
        {
            errors.Add("공정 코드는 30자를 넘을 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(request.ProcessName))
        {
            errors.Add("공정명을 입력하세요.");
        }
        else if (request.ProcessName.Length > 50)
        {
            errors.Add("공정명은 50자를 넘을 수 없습니다.");
        }

        return errors;
    }
}
