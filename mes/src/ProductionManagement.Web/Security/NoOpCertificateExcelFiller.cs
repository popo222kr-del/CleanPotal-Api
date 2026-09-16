using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;

namespace ProductionManagement.Web.Security;

// 성적서 Excel 채우기는 Excel COM(late binding)이라 Windows/설치된 Excel에 묶여 있어 서버(웹)에서
// 그대로 돌릴 수 없다(MS도 서버 측 Office 자동화를 공식 미지원). 웹에서는 우선 no-op으로 두어 DI 그래프만
// 성립시키고, 실제 성적서 생성은 이후 단계에서 ClosedXML/EPPlus 서버 구현 또는 별도 Windows 워커로 대체한다.
// (CertificateFillService가 이 경계를 요구하므로 등록만 채워주는 역할)
public sealed class NoOpCertificateExcelFiller : ICertificateExcelFiller
{
    public void Fill(string filePath, CertificateFillData data) { /* 웹에서는 미지원(추후 서버 구현으로 대체) */ }
    public void InsertImage(string filePath, string imagePath) { /* 웹에서는 미지원 */ }
}
