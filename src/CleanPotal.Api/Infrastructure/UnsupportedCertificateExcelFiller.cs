using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 성적서 엑셀에 값을 써 넣는 일은 데스크톱판이 Excel COM 으로 한다. 서버에는 Excel 이 없고,
/// 서버에서의 Office 자동화는 MS 도 지원하지 않는다. 그래서 웹에서는 아직 이 일을 못 한다.
///
/// 못 하는 것을 <b>못 했다고 답하게</b> 하려고 예외를 던진다. 아무 일도 하지 않고 조용히 돌아오면
/// (MES 웹판의 no-op 이 그랬다) 특이사항 이미지를 넣었다는 안내가 뜨는데 성적서에는 아무것도
/// 들어가 있지 않다 — 작업자는 넣었다고 믿고 넘어간다. 그게 제일 나쁜 결과다.
///
/// 부르는 쪽(CertificateFillService)은 예외를 삼키고 false 를 돌려주므로, 이 예외 때문에
/// 검사 저장이나 공정 이동이 막히지는 않는다.
/// </summary>
public sealed class UnsupportedCertificateExcelFiller : ICertificateExcelFiller
{
    private const string Reason = "웹에서는 성적서 엑셀에 값을 써 넣지 못합니다(데스크톱 프로그램에서만 가능).";

    public void Fill(string filePath, CertificateFillData data) => throw new NotSupportedException(Reason);

    public void InsertImage(string filePath, string imagePath) => throw new NotSupportedException(Reason);
}
