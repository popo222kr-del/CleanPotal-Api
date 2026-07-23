namespace CleanPotal.Core.DTOs;

public record BrokenRecordDto(
    int No,                 // 필터 내 순번
    int Id,
    DateOnly? OccurDate,
    string Line,
    string ProductName,
    string ProductType,
    string SN,
    string Team,
    string Causer,
    string JobTitle,
    string Career,
    string OccurStage,
    string Description,
    string Status,
    bool IsOfficial,
    bool PositionFrozen,
    string IncidentReports,
    string CountermeasureReports,
    string TrainingDocs,
    string TrainingImages,
    DateTime CreatedAt
);

public record BrokenUpsertRequest(
    DateOnly? OccurDate,
    string Line,
    string ProductName,
    string ProductType,
    string SN,
    string Team,
    string Causer,
    string JobTitle,
    string Career,
    string OccurStage,
    string Description,
    string Status,
    bool IsOfficial,
    bool PositionFrozen,
    string? IncidentReports,
    string? CountermeasureReports,
    string? TrainingDocs,
    string? TrainingImages
);

/// <summary>필터 옵션 (드롭다운 채우기용).</summary>
public record BrokenFilterOptionsDto(
    IReadOnlyList<int> Years,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> ProductTypes
);

// ── 교육 기록 ──
public record BrokenTrainingDto(int Id, string TrainingType, DateOnly? TrainingDate, string Content, string Documents, string Images);
public record BrokenTrainingUpsertRequest(string TrainingType, DateOnly? TrainingDate, string Content, string? Documents, string? Images);

// ── 교육 목표 / 메모 ──
public record BrokenGoalDto(int Id, string Category, int Year, string Target);
public record BrokenGoalInput(string Category, int Year, string Target);
public record BrokenMemoDto(string Memo);

/// <summary>유발자 직위/경력 자동 입력용 사용자 디렉터리.</summary>
public record BrokenUserDto(string RealName, string JobTitle, string HireDate);
