namespace CleanPotal.Core.Entities;

/// <summary>사용자/권한 변경 감사 로그 (관리자 조회 전용).</summary>
public class UserAuditLog
{
    public int Id { get; set; }
    public string TargetUser { get; set; } = "";   // 대상 (이름(아이디))
    public string Action { get; set; } = "";       // 생성/수정/권한변경/삭제/퇴사
    public string Detail { get; set; } = "";       // 변경 내용 (전→후)
    public string ByUser { get; set; } = "";       // 수행자
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
