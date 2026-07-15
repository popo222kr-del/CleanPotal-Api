namespace CleanPotal.Core.Entities;

/// <summary>ICP-MS 측정 1행 = (설비, 약액, 구분, 분석일). 원소 22종(ppb).</summary>
public class EquipmentAnalysis
{
    public int Id { get; set; }
    public string ProcessType { get; set; } = "";   // 설비 유형(엑셀 시트명)
    public string EqId { get; set; } = "";           // 설비 ID (MDC01 …)
    public string BathGb { get; set; } = "";         // 약액 (S2/HF/HNO3/DIW …)
    public string Category { get; set; } = "";       // 구분
    public string Unit { get; set; } = "ppb";
    public string AnalysisDate { get; set; } = "";   // yyyy-MM-dd

    // 원소 22종 (순서: Li Na Mg Al K Ca Ti Cr Mn Fe Co Ni Cu Zn Ge As Cd In Ba Ta W Pb)
    public double Li { get; set; }
    public double Na { get; set; }
    public double Mg { get; set; }
    public double Al { get; set; }
    public double K { get; set; }
    public double Ca { get; set; }
    public double Ti { get; set; }
    public double Cr { get; set; }
    public double Mn { get; set; }
    public double Fe { get; set; }
    public double Co { get; set; }
    public double Ni { get; set; }
    public double Cu { get; set; }
    public double Zn { get; set; }
    public double Ge { get; set; }
    public double As { get; set; }
    public double Cd { get; set; }
    public double In { get; set; }
    public double Ba { get; set; }
    public double Ta { get; set; }
    public double W { get; set; }
    public double Pb { get; set; }
}

/// <summary>설비 마스터. 측정 이력이 없어도 등록 가능.</summary>
public class EquipmentMaster
{
    public string EqId { get; set; } = "";     // PK
    public string Process { get; set; } = "";  // 공정/급 (A급, POLY(L10) …) — 차트 라벨 병기
}

/// <summary>점검 일지 특이사항 (설비 × 날짜). 복합 PK (EqId, CheckDate).</summary>
public class EquipmentCheckNote
{
    public string EqId { get; set; } = "";
    public string CheckDate { get; set; } = "";   // yyyy-MM-dd
    public string Note { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>감사 로그(마스터 조회 전용).</summary>
public class EquipmentActionLog
{
    public int Id { get; set; }
    public string ActionType { get; set; } = "";  // 엑셀 업로드/설비명 변경/공정 변경/특이사항 수정/설비 추가/전체 삭제
    public string Detail { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
