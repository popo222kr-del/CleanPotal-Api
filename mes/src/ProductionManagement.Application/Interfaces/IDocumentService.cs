using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 성적서 등 LOT에 딸린 문서 파일 관리. 파일 실체는 공유폴더에 두고 DB에는 경로만 남긴다.
// "조회 > 성적서 조회" 화면이 쓴다.
public interface IDocumentService
{
    Task<IReadOnlyList<DocumentDto>> GetByLotIdAsync(int lotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentDto>> SearchAsync(string? keyword, CancellationToken cancellationToken = default);
    Task<DocumentDto> UploadAsync(DocumentUploadRequest request, CancellationToken cancellationToken = default);
    string GetFullPath(DocumentDto document);
}
