namespace ProductionManagement.Domain.Entities;

// "고객사 LINE 정의" 시트 Master Data. 전산등록 화면의 LINE 입력란(현재는 자유 텍스트)이 앞으로 이
// 코드 목록을 참조하도록 확장할 예정 - 지금은 제품별 기본 LINE 지정(Product.DefaultLineId)까지만 반영.
public class LineDefinition : Entity<int>
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
