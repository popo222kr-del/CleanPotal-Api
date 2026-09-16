using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// SerialNumber는 전산등록 시 자동채번된 LOT 단위 S/N(Lot.SerialNumber)이다 - Product.SerialNumber와는
// 다른 값이다. ExportNumber/Line/ProcessLabel은 이 LOT을 만든 전산등록 레코드에서 가져온다(없으면 null).
// IsBatch/StageArrivedAt은 LotListItemDto와 같은 의미 (LotDto.cs 주석 참고).
// 2026-08-28 피드백(#4): 배치 대표 LOT을 클릭했을 때 옆 탭에 보여줄 "묶인 LOT" 한 줄(LOT번호 + S/N).
public record BatchMemberDto(int LotId, string LotNumber, string SerialNumber, bool IsRepresentative);

public record OperLotItemDto(
    int LotId,
    string LotNumber,
    string ProductCode,
    string ItemCode,
    string ProductName,
    string SerialNumber,
    string? CleaningCode,
    string CustomerName,
    // 업체가 속한 LINE(대분류) 코드 - Customer.LineDefinitionId 참고(2026-08-18 피드백: "LINE - 업체
    // 이렇게 생각해주면 돼"). 업체에 LINE이 배정되지 않았으면 null.
    string? CustomerLineCode,
    int Quantity,
    LotStatus CurrentStatus,
    bool IsBatch,
    DateTime ReceivedDate,
    DateTime StageArrivedAt,
    string? Worker,
    DateTime? StartedAt,
    string? ExportNumber,
    string? Line,
    string? ProcessLabel,
    double? TatHours,
    string? RecipeCode,
    string? EquipmentId,
    // Batch(2026-08-20) - 이 LOT이 배치 멤버면 그 대표 LOT의 Id/번호가 채워진다. 대표 LOT 자신이나
    // 배치에 속하지 않은 LOT은 둘 다 null(대표인지 여부는 IsBatch와 이 값이 null인 것을 같이 봐야 안다).
    int? RepresentativeLotId,
    string? RepresentativeLotNumber,
    // 2026-08-26: 이 LOT 제품의 Id - OPER RECIPE ID 드롭다운을 제품별 레시피로 채우기 위함(기본 0).
    int ProductId = 0,
    // 2026-08-28 피드백: OPER "업체명"은 매출 고객사가 아니라 전산등록의 PM설비명(업체명)을 따른다.
    // "분임조"는 품목코드 헤더 대체용(전산등록 분임조 값).
    string? PmEquipmentName = null,
    string? TeamName = null,
    // 2026-08-31 피드백(#6): 목록의 RECIPE 열은 코드가 아니라 레시피 명을 보여준다(셋업 목록 외 표기).
    string? RecipeName = null,
    // 2026-09-01 피드백(#5): 대시보드/목록의 "비고" 열 - 이 LOT의 가장 최근 코멘트(ProcessHistory.Comment).
    string? Comment = null,
    // 2026-09-01 피드백: 대시보드 목록의 "OPER" 열 - 이 LOT의 현재 공정(위치) 이름.
    string? CurrentProcessName = null,
    // 2026-09-03 피드백: 현재 공정에 배정된 레시피의 READ TIME(분). 값이 있으면(레시피 있는 공정) OPER 목록에
    // Start T/End T/Over T를 표기한다.
    int? ReadTimeMinutes = null)
{
    // Start T: 레시피가 있는 공정에서만(세정/건조/Bake 등) 작업 시작 시각을 표기. 레시피 없으면 null(빈칸).
    public DateTime? RecipeStartTime => ReadTimeMinutes is not null ? StartedAt : null;

    // End T: Start T + READ TIME = 레시피별 완료 예정 시각.
    public DateTime? RecipeEndTime =>
        RecipeStartTime is { } start && ReadTimeMinutes is { } minutes ? start.AddMinutes(minutes) : null;
}

public record OperLotSearchRequest(
    int ProcessDefinitionId,
    string? Keyword = null,
    int? CustomerId = null,
    int? ProductId = null,
    LotStatus? Status = null);

