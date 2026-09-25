using System.Text;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 글자(주소)를 QR 그림(SVG)으로. 라벨 인쇄용이라 이미지 파일 없이 벡터로 만들어 인쇄해도 흐려지지 않는다.
/// 인코더는 MES 가 바코드 읽기에 쓰는 ZXing.Net 을 같이 쓴다.
/// </summary>
public static class QrSvg
{
    public static string Render(string text, int margin = 2)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = margin,
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        var m = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints);
        var path = new StringBuilder();
        for (var y = 0; y < m.Height; y++)
            for (var x = 0; x < m.Width; x++)
                if (m[x, y]) path.Append('M').Append(x).Append(',').Append(y).Append("h1v1h-1z");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {m.Width} {m.Height}\" shape-rendering=\"crispEdges\">"
             + $"<rect width=\"{m.Width}\" height=\"{m.Height}\" fill=\"#fff\"/><path fill=\"#000\" d=\"{path}\"/></svg>";
    }
}
