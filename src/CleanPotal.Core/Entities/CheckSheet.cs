namespace CleanPotal.Core.Entities;

// ── QR 체크시트 (3정 5S 점검) ──
// 정의(구역·항목)와 기록(실적·결과)을 나눈다. 정의는 관리자가 화면이나 엑셀 가져오기로 바꾸고,
// 기록은 현장에서 QR 을 찍어 남긴다. 결과 행에는 그때의 항목 문구·기준을 함께 적어 두어,
// 나중에 항목을 고쳐도 과거 기록은 당시 내용 그대로 남는다.

/// <summary>점검 구역 — 방 하나가 QR 한 장이다. 공통 구역(예: M-ALL)은 QR 없이 같은 라인의 모든 구역 화면에 함께 뜬다.</summary>
public class CheckZone
{
    public int Id { get; set; }
    /// <summary>QR 주소에 들어가는 코드(예: M-OUT). 한 번 붙인 뒤에는 바꾸지 않는다.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>METAL / N-METAL / 공통.</summary>
    public string Line { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>공통 항목용 구역(QR 없음). 그 항목은 같은 라인의 모든 구역 화면에 뜨고 구역마다 따로 기록된다.</summary>
    public bool IsCommon { get; set; }
    public bool HasQr { get; set; } = true;
    public string QrLocation { get; set; } = "";
    public int QrCount { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>점검 항목 정의.</summary>
public class CheckItem
{
    public int Id { get; set; }
    /// <summary>항목 코드(예: M-012). 엑셀 가져오기의 기준 키.</summary>
    public string Code { get; set; } = "";
    public string ZoneCode { get; set; } = "";
    public int SortOrder { get; set; }
    public string Text { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>매일 / 매주 / 이벤트.</summary>
    public string Cycle { get; set; } = "";
    /// <summary>주·야 각 1회 / 주간조만 / 야간조만 / 주 1회 / 이벤트 발생 시.</summary>
    public string Timing { get; set; } = "";
    /// <summary>주 1회 항목의 요일(1=월 … 7=일). 비어 있으면 그 주 아무 날이나.</summary>
    public int? Weekday { get; set; }
    /// <summary>OKNG(체크) / NUM(수치).</summary>
    public string ResultType { get; set; } = CheckResultTypes.OkNg;
    public string Unit { get; set; } = "";
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    /// <summary>RANGE(하한~상한) / ABS(절댓값 ≤ 상한) / NONE.</summary>
    public string JudgeMode { get; set; } = CheckJudgeModes.None;
    /// <summary>없음 / NG 시 / 작업 전 / 작업 후 / 작업 전·후 / 항상.</summary>
    public string PhotoPolicy { get; set; } = CheckPhotoPolicies.OnNg;
    public bool Required { get; set; } = true;
    public bool AllowNa { get; set; }
    public string PaperForm { get; set; } = "";
    public string NgDept { get; set; } = "";
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string RevisionNote { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string UpdatedBy { get; set; } = "";
}

/// <summary>한 구역·근무일·교대의 점검 한 번. 항목 결과는 CheckResult 에 한 줄씩.</summary>
public class CheckRun
{
    public int Id { get; set; }
    public string ZoneCode { get; set; } = "";
    /// <summary>교대가 시작된 날짜(야간이 자정을 넘겨도 시작일).</summary>
    public DateOnly WorkDate { get; set; }
    /// <summary>주간 / 야간.</summary>
    public string Shift { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public string StartedBy { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
    public string SubmittedByName { get; set; } = "";
    /// <summary>QR 로 들어와 시작했는지(메뉴로 들어왔으면 false).</summary>
    public bool ViaQr { get; set; }
}

/// <summary>항목 하나의 결과. 항목 문구·기준은 기록 당시 값을 복사해 둔다.</summary>
public class CheckResult
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemText { get; set; } = "";
    public string ItemDetail { get; set; } = "";
    public string SpecText { get; set; } = "";
    /// <summary>OK / NG / NA. 사진만 먼저 올린 상태면 빈 값.</summary>
    public string Result { get; set; } = "";
    public decimal? NumValue { get; set; }
    public string Memo { get; set; } = "";
    /// <summary>사진 목록 JSON: [{"k":"before|after|ng|photo","v":"att:12|이름|image"}].</summary>
    public string Photos { get; set; } = "[]";
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string CheckedBy { get; set; } = "";
    public string CheckedByName { get; set; } = "";
    /// <summary>NG 조치 상태: "" / OPEN / DONE.</summary>
    public string NgStatus { get; set; } = "";
    public DateTime? NgClosedAt { get; set; }
    public string NgClosedBy { get; set; } = "";
    public string NgCloseNote { get; set; } = "";
}

/// <summary>체크시트 설정(교대 시각·QR 기본 주소·양식 이름 등). 키-값.</summary>
public class CheckSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public static class CheckResultTypes
{
    public const string OkNg = "OKNG";
    public const string Num = "NUM";
}

public static class CheckJudgeModes
{
    public const string Range = "RANGE";
    public const string Abs = "ABS";
    public const string None = "NONE";
}

public static class CheckPhotoPolicies
{
    public const string None = "없음";
    public const string OnNg = "NG 시";
    public const string Before = "작업 전";
    public const string After = "작업 후";
    public const string BeforeAfter = "작업 전·후";
    public const string Always = "항상";
    public static readonly string[] All = { None, OnNg, Before, After, BeforeAfter, Always };
}

public static class CheckTimings
{
    public const string BothShifts = "주·야 각 1회";
    public const string DayOnly = "주간조만";
    public const string NightOnly = "야간조만";
    public const string Weekly = "주 1회";
    public const string Event = "이벤트 발생 시";
    public static readonly string[] All = { BothShifts, DayOnly, NightOnly, Weekly, Event };
}
