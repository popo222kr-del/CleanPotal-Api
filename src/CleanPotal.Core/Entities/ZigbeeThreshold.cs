namespace CleanPotal.Core.Entities;

/// <summary>
/// 온·습도 판정 기준 한 벌. 어디에 걸리는지는 <see cref="Scope"/> 가 정한다.
///
///   global  전체 기본       ScopeKey = ""
///   site    사업장 단위      ScopeKey = "동탄"
///   device  센서 한 대       ScopeKey = "dongtan_1"
///
/// 좁은 쪽이 이긴다 — 센서 → 사업장 → 전체 → 설정 파일. 창고가 늘어나도 사업장 한 줄만 넣으면
/// 그 안의 센서가 전부 따라오고, 유독 다른 센서만 따로 잡아 줄 수 있다.
/// </summary>
public class ZigbeeThreshold
{
    public int Id { get; set; }

    /// <summary>global · site · device</summary>
    public string Scope { get; set; } = "global";

    /// <summary>사업장 이름 또는 센서 DeviceId. 전체 기본이면 빈 칸.</summary>
    public string ScopeKey { get; set; } = "";

    public double TempNormalMin { get; set; }
    public double TempNormalMax { get; set; }
    public double TempWarnMin { get; set; }
    public double TempWarnMax { get; set; }

    public double HumidNormalMin { get; set; }
    public double HumidNormalMax { get; set; }
    public double HumidWarnMin { get; set; }
    public double HumidWarnMax { get; set; }

    /// <summary>이 시간(분) 넘게 값이 없으면 통신 끊김.</summary>
    public int OfflineAfterMinutes { get; set; }

    /// <summary>이 값(%) 이하이면 배터리 부족.</summary>
    public int LowBatteryPercent { get; set; }

    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
