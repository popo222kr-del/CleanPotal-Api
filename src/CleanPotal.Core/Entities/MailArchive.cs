namespace CleanPotal.Core.Entities;

/// <summary>
/// 메일 확인(OFFICE › 메일 확인) — 웹메일 백업 ZIP 을 한 번 올린 기록. 본인 것만 보고 지운다.
/// </summary>
public class MailImport
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    public long Bytes { get; set; }
    /// <summary>ZIP 안의 메일(.eml) 수</summary>
    public int Total { get; set; }
    /// <summary>새로 보관한 메일 수</summary>
    public int Added { get; set; }
    /// <summary>이미 보관돼 있어 건너뛴 메일 수</summary>
    public int Duplicates { get; set; }
    /// <summary>읽지 못한 메일 수</summary>
    public int Failed { get; set; }
}

/// <summary>
/// 보관한 메일 한 통 — 목록·검색용 머리 정보만 DB 에 두고, 원본(.eml)은 메일 보관 폴더에 파일로 둔다.
/// 본문·첨부는 열 때 원본을 다시 읽는다.
/// </summary>
public class MailItem
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int ImportId { get; set; }
    /// <summary>원본 SHA-256(16진수) — 같은 사람에게 같은 메일이 두 번 들어가지 않게 한다.</summary>
    public string Hash { get; set; } = "";
    /// <summary>inbox 받은메일 · sent 보낸메일</summary>
    public string Folder { get; set; } = "inbox";
    public string Subject { get; set; } = "";
    public string FromName { get; set; } = "";
    public string FromAddr { get; set; } = "";
    /// <summary>받는 사람(이름 &lt;주소&gt;, 쉼표로 이음)</summary>
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    /// <summary>보낸 시각(서버 현지 시각). 머리글에 없으면 null.</summary>
    public DateTime? Date { get; set; }
    /// <summary>본문 앞부분(검색·목록 미리 보기)</summary>
    public string Preview { get; set; } = "";
    public int AttachmentCount { get; set; }
    public long Size { get; set; }
}
