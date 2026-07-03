namespace CleanPotal.Core.Entities;

/// <summary>BROKEN 교육 기록 (생산/물류). WPF TrainingRecordsProduction/Logistics.</summary>
public class BrokenTraining
{
    public int Id { get; set; }
    public string TrainingType { get; set; } = "production";  // production | logistics
    public DateOnly? TrainingDate { get; set; }
    public string Content { get; set; } = "";
    public string Documents { get; set; } = "";              // JSON
    public string Images { get; set; } = "";                 // JSON
    public int SortOrder { get; set; }
}

/// <summary>BROKEN 교육 목표 (연도별). WPF TrainingGoals.ProductionTargets/LogisticsTargets.</summary>
public class BrokenGoal
{
    public int Id { get; set; }
    public string Category { get; set; } = "production";     // production | logistics
    public int Year { get; set; }
    public string Target { get; set; } = "";
}

/// <summary>BROKEN 전역 메모 (단일 행). WPF broken_data.Memo.</summary>
public class BrokenMeta
{
    public int Id { get; set; }
    public string Memo { get; set; } = "";
}
