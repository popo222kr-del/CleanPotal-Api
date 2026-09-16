namespace ProductionManagement.Domain.Entities;

// 매출 고객사(업체). 세정을 맡기는 쪽이다.
// LINE(대분류) 아래 업체(소분류)가 여럿 묶이는 2단 구조이며, 반출번호는 이 업체의 ExportPrefix로 시작한다.
// "셋업 > 업체 관리" 화면에서 등록·수정한다.
public class Customer : Entity<int>
{
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    // 반출번호(ExportNumber) 접두어. 업체 등록 시 사용자가 직접 지정 (예: "삼성전자" -> "SS").
    public string ExportPrefix { get; set; } = string.Empty;

    // 업체가 속한 LINE(대분류) - "고객사 정의" 시트의 LINE DESC 컬럼. 실제 업체(소분류) 여럿이 하나의
    // LINE 아래 묶이는 구조다(2026-08-18 피드백: "LINE - 업체 이렇게 생각해주면 돼"). 제품 셋업의
    // Product.DefaultLineId(제품별 기본 LINE)와는 별개 - 업체 관리 화면에서 독립적으로 배정한다.
    public int? LineDefinitionId { get; set; }
    public LineDefinition? LineDefinition { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
