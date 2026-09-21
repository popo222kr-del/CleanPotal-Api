namespace CleanPotal.Core.Entities;

/// <summary>
/// 첨부 파일 한 개. 파일 자체는 디스크에 두고 여기에는 어디 있는지와 원래 이름만 남긴다.
///
/// 예전에는 파일을 base64 로 바꿔 기록 컬럼(nvarchar(max))에 통째로 넣었다.
/// base64 로 1.33배, nvarchar 가 글자당 2바이트라 다시 2배 — 1MB 파일이 DB 에서
/// 2.7MB 를 먹었다. 백업도 그만큼 커진다.
/// </summary>
public class Attachment
{
    public int Id { get; set; }

    /// <summary>디스크에 저장된 이름. 폴더 안에서만 유일하면 되므로 GUID 를 쓴다.</summary>
    public string StoredName { get; set; } = "";

    /// <summary>yyyyMM. 한 폴더에 파일이 수만 개 쌓이면 탐색이 느려진다.</summary>
    public string Folder { get; set; } = "";

    /// <summary>사람이 올릴 때 쓰던 이름. 받을 때 이 이름으로 준다.</summary>
    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long Size { get; set; }

    /// <summary>image | file — 화면이 사진으로 볼지 파일로 볼지 가른다.</summary>
    public string Kind { get; set; } = "file";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "";
}
