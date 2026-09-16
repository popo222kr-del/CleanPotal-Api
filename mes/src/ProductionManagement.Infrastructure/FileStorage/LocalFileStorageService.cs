using Microsoft.Extensions.Configuration;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Infrastructure.FileStorage;

// 저장 위치는 appsettings.{Environment}.json의 Documents:RootPath로만 정해진다 (하드코딩 금지).
// 2026-08-31 피드백: 날짜/LOT 하위폴더를 만들지 않고 RootPath(기타 Inspection)에 파일을 평면으로 누적
// 저장한다 - 파일을 한 곳에서 바로 찾기 위함. 파일명은 보통 {LOT번호}{확장자}라 LOT별로 구분된다.
// (기존에 {yyyy}/{MM}/{dd}/{LotNumber}/ 하위에 저장된 문서는 DB의 상대경로 그대로 GetFullPath로 계속 열린다.)
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _rootPath = configuration["Documents:RootPath"]
            ?? throw new InvalidOperationException("Documents:RootPath 설정이 필요합니다. appsettings.{Environment}.json을 확인하세요.");
    }

    public async Task<string> SaveAsync(string lotNumber, string sourceFilePath, DateTime receivedAt, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("선택한 파일을 찾을 수 없습니다.", sourceFilePath);
        }

        // 하위폴더 없이 RootPath에 바로 저장한다(평면 누적). receivedAt는 더 이상 경로에 쓰지 않는다.
        Directory.CreateDirectory(_rootPath);

        var fileName = Path.GetFileName(sourceFilePath);
        var targetPath = Path.Combine(_rootPath, fileName);

        // 같은 파일명이 이미 있으면 덮어쓰지 않고 새 이름으로 저장한다 (기존 성적서 보존).
        if (File.Exists(targetPath))
        {
            var nameOnly = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            targetPath = Path.Combine(_rootPath, $"{nameOnly}_{DateTime.Now:HHmmss}{extension}");
        }

        await using (var source = File.OpenRead(sourceFilePath))
        await using (var destination = File.Create(targetPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        // 상대경로 = 파일명(루트 기준). GetFullPath(RootPath + 파일명)으로 다시 연다.
        return Path.GetFileName(targetPath);
    }

    public string GetFullPath(string relativePath) => Path.Combine(_rootPath, relativePath);
}
