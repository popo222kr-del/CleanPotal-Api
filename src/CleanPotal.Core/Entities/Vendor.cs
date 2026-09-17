namespace CleanPotal.Core.Entities;

/// <summary>업체 마스터 (기존 WPF VendorModel). IsWeekly로 주간세정 대상 분류.</summary>
public class Vendor
{
    public int Id { get; set; }
    public string VendorName { get; set; } = "";
    public string Category { get; set; } = "일반";
    public bool IsWeekly { get; set; }
    public bool IsFavorite { get; set; }          // 즐겨찾기
    public string BasePath { get; set; } = "";    // 기본 경로
    public string LinkUrl { get; set; } = "";     // 업체 자체 시스템 링크 (URL)
    public string Addresses { get; set; } = "";   // 주소 여러 개 (JSON)
    public string Managers { get; set; } = "";    // 담당자 여러 개 (JSON)

    // 같은 업체의 MES 쪽 자료(MesCustomers.Id). 업체 관리 화면 하나에서 양쪽을 같이 다루기 위한 연결이다.
    // 이름으로 맞추지 않는 이유는 동명이인과 같은 문제다 — 이름이 바뀌거나 비슷하면 조용히 어긋난다.
    // 연결이 없으면 null(MES 를 쓰지 않는 업체이거나 아직 잇지 않은 것).
    public int? MesCustomerId { get; set; }

    // (레거시 — 실제 데이터엔 없음, 호환용)
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
}
