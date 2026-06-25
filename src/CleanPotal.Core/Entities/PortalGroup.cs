namespace CleanPotal.Core.Entities;

/// <summary>업무 파일 통합 관리 - 대분류 그룹.</summary>
public class PortalGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    public List<PortalItem> Items { get; set; } = new();
}
