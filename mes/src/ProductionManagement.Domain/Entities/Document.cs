namespace ProductionManagement.Domain.Entities;

// 파일 자체는 DB에 저장하지 않는다. 실제 파일은 IFileStorageService가 관리하는 공유폴더에 두고,
// 여기는 메타데이터(경로)만 가진다 (CLAUDE.md 7번). 새로 올릴 때마다 새 버전 행을 추가하고
// 기존 행은 그대로 둔다 - 성적서 변경 이력도 다른 이력과 마찬가지로 삭제/덮어쓰기하지 않는다.
public class Document : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int DocumentVersion { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
