using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Screens;

namespace ProductionManagement.Web.Services;

// 스캔(또는 입력)한 값으로 LOT을 찾아 "어느 OPER 화면에서 처리하면 되는지" 알려준다.
// 값은 LOT 번호가 기본이고, 앱의 LOT 현황 조회와 같은 조회 규칙(ILotHistoryService.FindLotIdByKeywordAsync)을 써서
// S/N·반출번호를 찍어도 찾는다. 공정 이동 자체는 하지 않는다 - 처리(TRAN)는 OPER 화면의 기존 게이트를 그대로 거친다.
public sealed class LotScanResolver
{
    private readonly ILotHistoryService _history;

    public LotScanResolver(ILotHistoryService history) => _history = history;

    public async Task<LotScanResult> ResolveAsync(string? scannedText, CancellationToken cancellationToken = default)
    {
        var code = LotScanCode.Normalize(scannedText);
        if (code is null)
        {
            return LotScanResult.NotFound(scannedText, "스캔한 값이 비어 있습니다.");
        }

        var lotId = await _history.FindLotIdByKeywordAsync(code, cancellationToken);
        if (lotId is null)
        {
            return LotScanResult.NotFound(code, $"'{code}'에 해당하는 LOT을 찾을 수 없습니다.");
        }

        var header = await _history.GetHeaderAsync(lotId.Value, cancellationToken);
        var hasScreen = OperScreens.Has(header.CurrentOperCode);
        return new LotScanResult(
            true, code, header.LotId, header.LotNumber, header.SerialNumber,
            header.CurrentOperCode, header.CurrentOperName, hasScreen,
            hasScreen ? null : $"LOT {header.LotNumber}은(는) 현재 {header.CurrentOperCode} {header.CurrentOperName} 단계라 OPER 화면에서 처리할 대상이 아닙니다.");
    }

}

public sealed record LotScanResult(
    bool Found,
    string? ScannedText,
    int LotId,
    string? LotNumber,
    string? SerialNumber,
    int OperCode,
    string? OperName,
    bool HasOperScreen,
    string? Message)
{
    public static LotScanResult NotFound(string? text, string message)
        => new(false, text, 0, null, null, 0, null, false, message);

    // 그 LOT이 있는 OPER 화면(도착하면 LOT이 바로 선택된다).
    public string OperUrl => $"oper/{OperCode}?lot={Uri.EscapeDataString(LotNumber ?? string.Empty)}";

    public string HistoryUrl => $"history?lot={Uri.EscapeDataString(LotNumber ?? string.Empty)}";
}
