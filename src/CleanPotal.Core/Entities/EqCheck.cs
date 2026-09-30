namespace CleanPotal.Core.Entities;

// ── 체크시트 (설비) — 설비 점검표 AQ-C-13 Rev.7 ──
// 점검표는 설비 유형(DIP·SiC BAKE·DRY OVEN …)마다 같은 양식이라 '양식(유형) → 항목' 으로 두고 호기마다 양식을 고른다.
// 주기: 일상(하루 1회) · 주간(금요일 09시) · 월간(그 달 안에). 일상·주간은 생산팀, 월간은 설비팀이 한다.
// 결과 행에는 그때의 항목 문구·기준을 복사해 두어, 나중에 양식을 고쳐도 지난 기록은 당시 내용 그대로 남는다.

/// <summary>점검표 양식(설비 유형) — 예: DIP CLEANER, SIC BAKE OVEN.</summary>
public class EqCheckTemplate
{
    public int Id { get; set; }
    /// <summary>양식 코드(예: DIP, DIP-H, BAKE). 기본 양식 채우기의 기준 키.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = "";
}

/// <summary>점검 항목. 입력 방식: OXA(O/△/X) · CHOICE(보기 고르기) · NUM(수치 1칸) · MULTI(수치 여러 칸).</summary>
public class EqCheckItem
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    /// <summary>일상 / 주간 / 월간.</summary>
    public string Cycle { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>대분류(설비 공조 상태·게이지·오염원 추적제거·배관·구동부·안전점검 …).</summary>
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>같은 항목 안의 측정 위치(예: #1 DI Bath, ( L ), Supply 1번 배관). 없으면 빈 칸.</summary>
    public string Point { get; set; } = "";
    /// <summary>판정 기준 문구(점검표 그대로).</summary>
    public string Spec { get; set; } = "";
    public string InputType { get; set; } = EqInputTypes.Oxa;
    /// <summary>
    /// CHOICE 보기 — '|' 로 나눈다. 첫 보기가 정상, 나머지는 NG. 앞에 '*' 를 붙인 보기는 '조치함'
    /// (예: *비정기 세정진행) — NG 로 남기되 바로 조치 완료로 닫는다.
    /// </summary>
    public string Options { get; set; } = "";
    /// <summary>MULTI 칸 이름 — '|' 로 나눈다(예: Set|Real, Main|1|2|4|5). 첫 칸과의 편차로 판정한다.</summary>
    public string Fields { get; set; } = "";
    public string Unit { get; set; } = "";
    /// <summary>NUM: 하한·상한. MULTI: Max 가 허용 편차(±).</summary>
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    /// <summary>가동 중에만 재는 항목(예: 가동: 30±5 ℓ/min) — '비가동' 을 고를 수 있다.</summary>
    public bool RunOnly { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>점검하는 호기 — 설비 목록(스케줄 보드 설비)의 호기 코드(챔버 번호를 뗀 것)와 양식을 잇는다. QR 1장 = 호기 1대.</summary>
public class EqCheckUnit
{
    public int Id { get; set; }
    /// <summary>호기 코드(예: NDC01, MBO01, MSC02, SUP-HF). QR 주소 /e/{코드}.</summary>
    public string Code { get; set; } = "";
    public int TemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>호기·주기·기간 한 번의 점검(일상=날짜, 주간=그 주 금요일, 월간=yyyy-MM). 특이사항을 둔다.</summary>
public class EqCheckRecord
{
    public int Id { get; set; }
    public string UnitCode { get; set; } = "";
    public string Cycle { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = "";
}

/// <summary>항목 하나의 결과(또는 점검과 따로 적은 고장 — ItemId 0). 항목 문구·기준은 기록 당시 값을 복사해 둔다.</summary>
public class EqCheckResult
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public string UnitCode { get; set; } = "";
    public string Cycle { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public int ItemId { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Point { get; set; } = "";
    public string Spec { get; set; } = "";
    public string InputType { get; set; } = "";
    /// <summary>OXA: O/△/X · CHOICE: 고른 보기 · NUM/MULTI: 빈 칸(값은 Nums).</summary>
    public string Value { get; set; } = "";
    /// <summary>수치 JSON — {"칸 이름": 값}. NUM 은 {"": 값}.</summary>
    public string Nums { get; set; } = "";
    public bool NotRunning { get; set; }
    /// <summary>OK / NG.</summary>
    public string Judge { get; set; } = "";
    public string Memo { get; set; } = "";
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string CheckedBy { get; set; } = "";
    public string CheckedByName { get; set; } = "";
    /// <summary>NG 조치 상태: "" / OPEN / DONE.</summary>
    public string NgStatus { get; set; } = "";
    public DateTime? NgClosedAt { get; set; }
    public string NgClosedBy { get; set; } = "";
    public string NgCloseNote { get; set; } = "";
}

public static class EqInputTypes
{
    public const string Oxa = "OXA";
    public const string Choice = "CHOICE";
    public const string Num = "NUM";
    public const string Multi = "MULTI";
    public static readonly string[] All = { Oxa, Choice, Num, Multi };
}

public static class EqCycles
{
    public const string Daily = "일상";
    public const string Weekly = "주간";
    public const string Monthly = "월간";
    /// <summary>점검과 따로 적은 고장·부적합(점검표 아래 '점검 부적합 및 고장 조치 내용').</summary>
    public const string Fault = "고장";
    public static readonly string[] All = { Daily, Weekly, Monthly };
}
