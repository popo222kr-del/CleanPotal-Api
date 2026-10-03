namespace CleanPotal.Core.Entities;

/// <summary>
/// 로그인 기록 — 누가 언제 어디서(사내/사외·IP) 로그인했는지, 실패·차단까지. 관리자 › 외부 접속 보안에서 본다.
/// 매 요청이 아니라 로그인 시도마다 한 줄(양이 많지 않다). 오래된 줄은 180일 뒤 지운다.
/// </summary>
public class AccessLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public string RealName { get; set; } = "";
    public string Ip { get; set; } = "";
    /// <summary>사외(사내 IP 대역 밖)에서 온 시도인가</summary>
    public bool External { get; set; }
    /// <summary>ok 성공 · fail 비밀번호 틀림 · blocked 사외 접속 불가 계정 · throttled 시도 너무 많음</summary>
    public string Result { get; set; } = "";
    public string UserAgent { get; set; } = "";
}
