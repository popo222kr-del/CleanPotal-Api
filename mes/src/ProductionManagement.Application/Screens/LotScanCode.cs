namespace ProductionManagement.Application.Screens;

/// <summary>
/// 스캐너·QR 에서 읽은 값을 LOT 조회에 쓸 수 있는 한 줄로 정리한다.
/// 포털(React 화면)과 MES(Blazor 화면)가 같은 규칙을 써야 같은 라벨이 같게 읽힌다.
/// </summary>
public static class LotScanCode
{
    /// <summary>앞뒤 공백·줄바꿈을 정리한다. 여러 줄이면 첫 줄만 LOT 값으로 본다. 비면 null.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var firstLine = text.Trim().Split('\n', '\r')[0].Trim();
        return firstLine.Length == 0 ? null : firstLine;
    }
}
