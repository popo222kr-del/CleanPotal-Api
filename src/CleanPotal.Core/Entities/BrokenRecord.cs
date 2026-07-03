namespace CleanPotal.Core.Entities;

/// <summary>BROKEN(파손/불량) 관리 기록 (기존 WPF BrokenRecord).</summary>
public class BrokenRecord
{
    public int Id { get; set; }
    public DateOnly? OccurDate { get; set; }
    public string Line { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductType { get; set; } = "";   // 제품군
    public string SN { get; set; } = "";
    public string Team { get; set; } = "";           // 생산 / 물류 등
    public string Causer { get; set; } = "";         // 유발자
    public string JobTitle { get; set; } = "";       // 직위 스냅샷
    public string Career { get; set; } = "";         // 경력 스냅샷
    public string OccurStage { get; set; } = "";     // 발생 공정/단계
    public string Description { get; set; } = "";     // 내용
    public string Status { get; set; } = "접수";      // 접수 / 조치중 / 완료
    public bool IsOfficial { get; set; }             // 공식(true) / 비공식(false)
    public bool PositionFrozen { get; set; }         // 직위/경력 입력 시점 고정 여부

    // 첨부(발생/대책 보고서·교육자료·이미지) — JSON 문자열로 보존
    public string IncidentReports { get; set; } = "";
    public string CountermeasureReports { get; set; } = "";
    public string TrainingDocs { get; set; } = "";
    public string TrainingImages { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
