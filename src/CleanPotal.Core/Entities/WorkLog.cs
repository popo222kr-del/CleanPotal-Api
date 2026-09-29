namespace CleanPotal.Core.Entities;

// ── 업무 파일 통합 관리 ──
// 엑셀로 관리하던 세정·BAKE 업무 기록(약액 교체·업무보고·가성소다/폐액·BAKE 그을음·폐기품)을 웹으로 옮긴 것.
// 설비 목록은 한 곳(WorkEquipment)에 두고 각 기록이 설비 코드로 가리킨다.

/// <summary>업무 기록용 설비(세정 DIP/SPRAY 설비·BAKE 오븐). 업무보고의 설비·공정 목록과 같다.</summary>
public class WorkEquipment
{
    public int Id { get; set; }
    /// <summary>설비 코드(예: MDC01, NSC01-1, MBO01-1). 기록은 이 코드로 설비를 가리킨다.</summary>
    public string Code { get; set; } = "";
    /// <summary>METAL / N-METAL.</summary>
    public string Line { get; set; } = "";
    /// <summary>세정 / BAKE.</summary>
    public string Kind { get; set; } = "";
    /// <summary>공정(예: POLY(L10), Hot Chemical(L30), BAKE).</summary>
    public string Process { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>쓰지 않는 설비는 지우지 않고 끈다 — 지난 기록이 가리키고 있다.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 약액(CHEMICAL) 교체·설비 변경점 한 칸 = (날짜, 설비). 엑셀 "CHEMICAL 교체 및 설비 변경점" 의 한 칸.
/// Content 는 교체 내용(예: "S2 100%, HF 100%"), Note 는 설비 변경점 등 메모(엑셀의 셀 메모).
/// </summary>
public class ChemicalChange
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string EqCode { get; set; } = "";
    public string Content { get; set; } = "";
    public string Note { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 가성소다·폐액량 한 줄 = (날짜, 교대 주/야). 엑셀 "가성소다, 폐액 증가량 및 약액 교체 현황" 의 한 줄.
/// 감소량(가성소다 前−現)·증가량(폐액 現−前)은 저장하지 않고 계산한다. 비어 있는 값은 null(0 과 다르다).
/// </summary>
public class WasteLog
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>주 / 야.</summary>
    public string Shift { get; set; } = "";
    public decimal? CausticBefore { get; set; }
    public decimal? CausticAfter { get; set; }
    public decimal? WasteBefore { get; set; }
    public decimal? WasteAfter { get; set; }
    /// <summary>그 교대에 약액을 교체한 Dip 설비(예: "NDC02, MDC01").</summary>
    public string DipEquipment { get; set; } = "";
    /// <summary>그 교대에 약액을 교체한 Spray 설비(예: "NSC01-1").</summary>
    public string SprayEquipment { get; set; } = "";
    /// <summary>일교체량.</summary>
    public decimal? DailyChange { get; set; }
    public string Note { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// BAKE OVEN 그을음 기록 한 칸 = (날짜, 교대, 회차, 오븐). 엑셀 "BAKE OVEN 그을음 현황" 의 한 블록 × 오븐 열.
/// 한 교대에 오븐을 두 번 돌리기도 해서 엑셀에 같은 "9/28 (야간)" 블록이 여러 개 있다 — 그 순서가 회차(1, 2, …)다.
/// Status 가 빈 값이면 가동(투입~배출 기록), "비가동"·"HOLD"·"PM" 등이면 그 상태만 적은 칸.
/// </summary>
public class BakeLog
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>주 / 야.</summary>
    public string Shift { get; set; } = "";
    /// <summary>같은 교대 안의 회차(1부터).</summary>
    public int Round { get; set; } = 1;
    public string EqCode { get; set; } = "";
    /// <summary>빈 값 = 가동. 그 밖(비가동·HOLD·PM 등)은 가동하지 않은 까닭.</summary>
    public string Status { get; set; } = "";
    public DateTime? TrackIn { get; set; }
    public DateTime? TrackOut { get; set; }
    /// <summary>품명(예: (C)BS_BOAT_144_MASKPOLY).</summary>
    public string Item { get; set; } = "";
    public string SerialNo { get; set; } = "";
    /// <summary>그을음 — "X" 는 없음, "O" 나 위치 설명은 있음.</summary>
    public string Soot { get; set; } = "";
    /// <summary>온도 ↑(예: PN2 30).</summary>
    public string TempUp { get; set; } = "";
    /// <summary>온도 ↓ 첫 줄(예: CN2 0).</summary>
    public string TempDown { get; set; } = "";
    /// <summary>온도 ↓ 둘째 줄(예: PN2 30).</summary>
    public string TempDown2 { get; set; } = "";
    /// <summary>Q'TZ 가루 — "無" 는 없음, "有" 는 있음.</summary>
    public string Quartz { get; set; } = "";
    public string Note { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
