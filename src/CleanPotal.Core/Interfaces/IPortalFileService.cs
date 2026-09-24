namespace CleanPotal.Core.Interfaces;

/// <summary>
/// 업무 파일 바로가기의 실제 파일을 안전하게 해석한다.
/// 브라우저가 임의 경로를 보내지 못하도록 PortalItem ID만 받고,
/// 설정된 공유폴더 하위의 파일만 반환한다.
/// </summary>
public interface IPortalFileService
{
    Task<PortalFileDescriptor?> ResolveAsync(int itemId, CancellationToken cancellationToken = default);
}

public sealed record PortalFileDescriptor(string FullPath, string FileName);
