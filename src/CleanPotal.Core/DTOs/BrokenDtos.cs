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
    DateTime CreatedAt,
    int RowVersion
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
    string? TrainingImages,
    int? RowVersion = null   // 받아 간 버전. 비우면(옛 화면) 확인하지 않는다
);

/// <summary>필터 옵션 (드롭다운 채우기용).</summary>
public record BrokenFilterOptionsDto(
    IReadOnlyList<int> Years,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> ProductTypes
);

/// <summary>
/// 등록 칸 드롭다운 목록. ProductTypes·OccurStages 만 편집 대상이고,
/// Teams·Lines 는 조직 관리·MES 라인에서 읽어 오는 것이라 여기서 고칠 수 없다.
/// </summary>
public record BrokenOptionsDto(
    IReadOnlyList<string> ProductTypes,
    IReadOnlyList<string> OccurStages,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Lines
);

/// <summary>편집 가능한 목록만 담는 저장 요청.</summary>
public record BrokenOptionsSaveRequest(
    IReadOnlyList<string>? ProductTypes,
    IReadOnlyList<string>? OccurStages
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
