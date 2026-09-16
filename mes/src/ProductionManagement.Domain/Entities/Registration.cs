namespace ProductionManagement.Domain.Entities;

// "전산등록" 1건 = 이 레코드 1건. 이 레코드가 낳는 Lot은 항상 RequestedQuantity만큼(품목/조건과
// 무관하게 각각 별도 Lot) 생성된다 - RegistrationService 참고.
public class Registration : Entity<int>
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // {Customer.ExportPrefix}{등록일자:yyMMdd}-{업체별 당일 순번}. 시스템이 발급하며 이후 수기 수정 가능
    // (수정 시 유일성은 RegistrationService.UpdateAsync에서 재검증).
    public string ExportNumber { get; set; } = string.Empty;

    public string Line { get; set; } = string.Empty;

    // 화면/사용자 표기상 "PROCESS"(POLY/OXIDE 등 자유 텍스트). ProcessDefinition/ProcessRoute와
    // 이름이 겹치지 않도록 ProcessLabel로 명명 - 실제 공정 경로 결정은 ProcessRouteId가 담당하고
    // 이 값은 표시용 라벨일 뿐이다.
    public string ProcessLabel { get; set; } = string.Empty;

    public int ProcessRouteId { get; set; }
    public ProcessRoute ProcessRoute { get; set; } = null!;

    public int RequestedQuantity { get; set; }

    // 고객출고일 = 등록일자.
    public DateTime ShipDate { get; set; }

    public string PmEquipmentName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;

    public string RegisteredBy { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
}
