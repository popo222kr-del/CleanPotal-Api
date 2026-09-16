using Microsoft.Extensions.Logging;
using ProductionManagement.Application.Exceptions;

namespace ProductionManagement.Web.Models;

// 화면 오류 문구 통일(2026-09-15 웹 QA #5). 서비스 예외를 사용자에게 보여줄 문장으로 바꾼다.
//  - 검증 오류 / 공정 전이 오류: 서비스가 사용자용으로 만든 문장을 그대로.
//  - 권한 없음 / 동시 수정 충돌: 정해진 안내.
//  - 그 밖(DB·네트워크 등 예상 못 한 예외): 기술 문구(예외 메시지)는 숨기고 일반 안내 + 원인은 로그로.
public static class UiErrors
{
    public const string General = "처리 중 문제가 발생했습니다. 잠시 후 다시 시도해 주세요.";

    public static string ToMessage(Exception ex, ILogger logger, string action)
    {
        switch (ex)
        {
            case ValidationException validation:
                return string.Join(" / ", validation.Errors);
            case InvalidProcessTransitionException transition:
                return transition.Message;
            case UnauthorizedException:
                return "권한이 없습니다.";
            case ConcurrencyConflictException:
                return "다른 사용자가 먼저 변경했습니다. 목록을 새로 고친 뒤 다시 시도해 주세요.";
            default:
                logger.LogError(ex, "웹 화면 작업 실패: {Action}", action);
                return General;
        }
    }
}
