namespace ProductionManagement.Application.Interfaces;

// 실제 파일 복사/경로 조합만 담당한다. DB에는 이 결과로 나온 상대경로만 저장된다 (CLAUDE.md 7번).
// Windows 표준 파일 접근만 쓰고 문서보안/DRM을 우회하지 않는다 (CLAUDE.md 절대 금지사항).
public interface IFileStorageService
{
    Task<string> SaveAsync(string lotNumber, string sourceFilePath, DateTime receivedAt, CancellationToken cancellationToken = default);
    string GetFullPath(string relativePath);
}
