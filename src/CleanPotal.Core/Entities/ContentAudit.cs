namespace CleanPotal.Core.Entities;

/// <summary>
/// 업무 자료 변경 이력. "누가 언제 무엇을 바꿨는지"를 남긴다.
/// 계정·권한 변경 이력은 별도 테이블(<see cref="UserAuditLog"/>)에 남는다.
/// </summary>
public class ContentAudit
{
    public int Id { get; set; }

    /// <summary>대상 종류 — "공지", "인수인계", "생산요청", "생산미팅".</summary>
    public string EntityType { get; set; } = "";

    /// <summary>대상 행의 Id.</summary>
    public int EntityId { get; set; }

    /// <summary>생성 / 수정 / 삭제 / 상태변경.</summary>
    public string Action { get; set; } = "";

    /// <summary>변경 요약(무엇이 어떻게 바뀌었는지). 본문 전체를 담지 않는다.</summary>
    public string Detail { get; set; } = "";

    /// <summary>수행자 계정 PK. 과거 데이터 보정 중이면 null 일 수 있다.</summary>
    public int? ByUserId { get; set; }

    /// <summary>수행자 실명(표시용 캐시).</summary>
    public string ByUserName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
