namespace CleanPotal.Core.Entities;

/// <summary>업무 파일 통합 관리 - 바로가기 항목.</summary>
public class PortalItem
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public PortalGroup? Group { get; set; }

    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "folder";  // folder / excel / ppt / pdf / web
    public int SortOrder { get; set; }
}
