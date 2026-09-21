namespace CleanPotal.Core.DTOs;

/// <summary>올린 파일 한 개의 정보. Ref 를 기록 칸에 그대로 적어 두면 된다.</summary>
public record AttachmentDto(
    int Id,
    string FileName,
    string ContentType,
    long Size,
    string Kind,
    /// <summary>기록 칸에 담는 문자열. att:&lt;id&gt;|&lt;이름&gt;|&lt;종류&gt;</summary>
    string Ref
);
