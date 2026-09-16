using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 런시트(공정 진행표)를 새 Excel 파일로 생성한다. 성적서와 달리 DRM이 아닌 일반 xlsx라 ClosedXML로 만든다.
// 구현은 ClosedXML을 참조하는 Certificate 프로젝트(net10.0-windows)에 둔다.
public interface IRunsheetGenerator
{
    // 임시 폴더에 런시트 xlsx를 만들고 그 파일 경로를 돌려준다.
    string Generate(RunsheetData data);
}
