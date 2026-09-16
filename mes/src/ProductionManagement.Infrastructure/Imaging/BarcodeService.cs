using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.SkiaSharp;

namespace ProductionManagement.Infrastructure.Imaging;

// LOT 바코드/QR 해독·생성(ZXing.Net + SkiaSharp, 서버에서 처리).
// 모바일은 사내 Wi-Fi에서 HTTP로 접속하는데, 브라우저 실시간 카메라(getUserMedia/BarcodeDetector)는 HTTPS에서만 허용된다.
// 그래서 휴대폰 카메라로 "사진"을 찍어 올리면 서버가 읽는 방식을 쓴다(HTTP에서도 동작).
public sealed class BarcodeService
{
    // 휴대폰 사진(수천 px)은 그대로 읽으면 느리고 오히려 실패가 잦다 - 긴 변을 이 크기로 줄여 먼저 시도한다.
    private const int MaxDecodeEdge = 1600;

    private static readonly IList<BarcodeFormat> Formats = new[]
    {
        BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
        BarcodeFormat.DATA_MATRIX, BarcodeFormat.EAN_13, BarcodeFormat.ITF,
    };

    // 이미지에서 첫 번째 바코드/QR 값을 읽는다. 못 읽으면 null.
    public string? Decode(byte[] imageBytes)
    {
        using var original = SKBitmap.Decode(imageBytes);
        if (original is null)
        {
            return null;
        }

        var text = TryDecodeScaled(original) ?? TryDecode(original);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // LOT 라벨·화면 표시용 QR PNG.
    public byte[] EncodeQrPng(string text, int size = 240)
    {
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = size, Height = size, Margin = 1, CharacterSet = "UTF-8" },
        };
        using var bitmap = writer.Write(text);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string? TryDecodeScaled(SKBitmap source)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= MaxDecodeEdge)
        {
            return null; // 원본 시도에서 읽는다.
        }

        var scale = (double)MaxDecodeEdge / longEdge;
        var info = new SKImageInfo((int)(source.Width * scale), (int)(source.Height * scale));
        using var scaled = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear));
        return scaled is null ? null : TryDecode(scaled);
    }

    private static string? TryDecode(SKBitmap bitmap)
    {
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = Formats },
        };
        return reader.Decode(bitmap)?.Text;
    }
}
