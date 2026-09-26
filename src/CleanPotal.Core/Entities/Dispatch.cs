namespace CleanPotal.Core.Entities;

/// <summary>배차 기록 (WPF dispatch.db DispatchList). 인수인계·자재물류 배차 불러오기의 원천.</summary>
public class Dispatch
{
    public int Id { get; set; }
    public string VendorName { get; set; } = "";
    public string OutgoingDetails { get; set; } = "";   // 출고 내역
    public string IncomingDetails { get; set; } = "";   // 입고 내역
    public string ManagerName { get; set; } = "";       // 담당자
    public string ContactNumber { get; set; } = "";     // 연락처
    public string FullAddress { get; set; } = "";       // 주소
    public string Note { get; set; } = "";
    public DateTime CreateDate { get; set; } = DateTime.Now;

    /// <summary>동시 수정 감지용 버전 — 같은 행을 두 사람이 고치면 뒤 사람이 앞 사람 것을 덮지 않게.</summary>
    public int RowVersion { get; set; }
}
