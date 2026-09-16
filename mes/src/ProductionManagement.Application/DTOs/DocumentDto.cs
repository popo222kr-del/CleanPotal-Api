namespace ProductionManagement.Application.DTOs;

// 성적서 등 LOT 문서 관련 자료 묶음.
//   DocumentDto           목록에 뿌리는 문서 한 건(파일명/버전/등록자/경로)
//   DocumentUploadRequest 올릴 파일의 원본 경로와 붙일 LOT
public record DocumentDto(
    int DocumentId,
    int LotId,
    string LotNumber,
    string FileName,
    string FilePath,
    int DocumentVersion,
    string CreatedBy,
    DateTime CreatedAt);

public record DocumentUploadRequest(int LotId, string SourceFilePath);
