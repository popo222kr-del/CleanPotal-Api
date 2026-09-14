using CleanPotal.Core.Interfaces;

namespace CleanPotal.Core.Security;

/// <summary>
/// 자료 단위 권한 규칙 — "이 글을 이 사람이 손대도 되는가".
///
/// 영역 등급(0/1/2)은 <c>DbPermissionHandler</c> 가 먼저 검사한다. 여기는 그 다음 단계로,
/// **작성자 본인 또는 관리자**여야 하는 동작에만 적용한다.
///
/// 적용 방침(업무 결정 사항):
/// - 인수인계·생산요청·생산미팅처럼 <b>여러 사람이 이어서 채우는 공동 업무</b>는
///   등급 2 면 누구나 <b>수정</b>할 수 있다(여기서 막지 않는다).
/// - 공지처럼 <b>작성자 책임이 중요한 항목</b>은 수정도 작성자/관리자로 제한한다.
/// - <b>삭제는 수정과 별도</b>로, 공동 업무 항목이라도 작성자/관리자만 할 수 있다.
///   (잘못 지우면 복구할 수 없고, 공동 수정과 달리 되돌릴 여지가 없다)
/// </summary>
public static class ContentOwnership
{
    /// <summary>작성자 본인이거나 관리자인가.</summary>
    public static bool IsOwnerOrAdmin(ICurrentUser me, int? creatorUserId, string? creatorName)
    {
        if (me.IsAdmin) return true;
        if (me.Id is null) return false;

        // 작성자 ID 가 기록된 행 — 이름이 같거나 바뀌어도 정확히 판정된다.
        if (creatorUserId is not null) return creatorUserId == me.Id;

        // 작성자 정보가 아예 없는 과거 행(ID 도 이름도 비어 있음)은 '작성자 미상'으로 보고 통과시킨다.
        // 여기서 막으면 회의록·주간보고처럼 작성자를 기록한 적이 없는 기존 자료를
        // 관리자 외에는 아무도 정리할 수 없게 되어 업무가 멈춘다.
        // 새로 만드는 행은 항상 작성자 ID 가 채워지므로 이 예외는 과거 데이터에만 적용된다.
        if (string.IsNullOrWhiteSpace(creatorName)) return true;

        // 작성자 ID 가 아직 비어 있는 과거 행 — 이름으로 대조(backfill-authors 전까지의 임시 경로).
        // 이름은 동명이인·개명에 취약하므로 새 행에는 반드시 ID 를 채운다.
        return string.Equals(creatorName.Trim(), me.RealName.Trim(), StringComparison.Ordinal);
    }

    /// <summary>작성자/관리자가 아니면 403. <paramref name="what"/> 는 "공지", "인수인계" 등.</summary>
    public static void EnsureOwnerOrAdmin(ICurrentUser me, int? creatorUserId, string? creatorName, string what, string action)
    {
        if (IsOwnerOrAdmin(me, creatorUserId, creatorName)) return;
        throw new ForbiddenException($"{what}은(는) 작성자 본인 또는 관리자만 {action}할 수 있습니다.");
    }
}
