using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// AETS 성적서(Excel) 파일 한 개를 열어 앱 연동 영역 셀을 채워 저장하는 경계(boundary).
// 실제 구현은 Excel COM(late binding)이라 net10.0-windows인 Certificate 프로젝트에 둔다 - Infrastructure
// (net10.0)는 이 인터페이스만 알고 COM에는 의존하지 않는다. DRM 우회 없이 설치된 Excel로만 연다.
public interface ICertificateExcelFiller
{
    // filePath: LOT별 성적서 파일(생성 시 템플릿을 복사해 둔 그 파일). 제자리(in-place)로 채워 저장한다.
    void Fill(string filePath, CertificateFillData data);

    // 특이사항(ABNORMAL) 이미지를 성적서의 자유 영역(섹션 아래)에 삽입해 저장한다(2026-08-27 피드백:
    // 특이사항은 성적서 Excel에 삽입). 여러 장 넣으면 아래로 쌓인다.
    void InsertImage(string filePath, string imagePath);
}
