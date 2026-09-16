using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// 과거 이력은 UPDATE로 덮어쓰지 않는다. 공정이 진행/재작업될 때마다 새 Row를 추가한다 (CLAUDE.md 7번).
public class ProcessHistory : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public string Worker { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public LotStatus Status { get; set; }
    public ProcessResult? Result { get; set; }
    public int Quantity { get; set; }
    public int DefectQuantity { get; set; }
    public string? Remarks { get; set; }

    // 같은 공정을 재세정/재건조 등으로 다시 시도할 때 1차/2차 구분 (CLAUDE.md 절대 금지사항: 재작업 이력 유지).
    public int AttemptNumber { get; set; } = 1;

    // 이 시도가 실제로 어느 설비호기에 적재되어 진행됐는지(RES ID) - 세정/건조처럼 여러 설비가 있는
    // 공정에서 작업자가 수기로 입력한다. Registration.PmEquipmentName(PM 설비명, 전산등록 시 고정 입력)과는
    // 다른 값이다 - 이건 매 시도(Attempt)마다 실제 진행 설비가 달라질 수 있어 ProcessHistory에 둔다.
    public string? EquipmentId { get; set; }

    // OPER 화면 "제품 정보" 영역의 CMT_AETS(코멘트 작성 칸). Remarks는 TRAN 실행 시 자동으로 채워지는
    // 사유/특이사항 기록이라 용도가 다르다 - 이건 작업자가 그 화면에서 자유롭게 남기는 별도 메모.
    public string? Comment { get; set; }

    // 이 시도(Attempt)에서 실제로 사용한 레시피 스냅샷 - 세정/건조 OPER에서만 채워진다. 제품의 "현재
    // 기본 레시피"는 ProductRecipeAssignment가 따로 갖고 있지만(제품 셋업 화면 표시용), 같은 Lot이
    // 재세정 등으로 여러 번 시도될 때 매 시도마다 실제 쓴 레시피가 달라질 수 있어 "LOT 현황 조회"
    // 이력에는 이 값을 그대로 남긴다(2026-08-18 피드백: TRAN 이력에 RECIPE ID 표시).
    public int? RecipeDefinitionId { get; set; }
    public RecipeDefinition? RecipeDefinition { get; set; }

    // 2026-08-20 "이력 삭제"(TRAN 무효화) - 이 이력을 DB에서 지우지 않고(CLAUDE.md 절대 금지사항: 이력
    // 삭제 금지) "무효화됨" 표시만 남긴다. 무효화 가능 조건/LOT 상태 복원 로직은
    // ProcessHistoryVoidService 참고.
    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}
