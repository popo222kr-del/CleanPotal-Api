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
    bool IsOfficial
);

/// <summary>필터 옵션 (드롭다운 채우기용).</summary>
public record BrokenFilterOptionsDto(
    IReadOnlyList<int> Years,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> ProductTypes
);
