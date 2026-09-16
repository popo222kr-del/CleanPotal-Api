using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 2026-08-31 피드백(#11): 고객 출하된 제품들을 따로 엑셀로 만들어 이력 관리한다.
// 성적서(DRM)와 달리 일반 xlsx라 ClosedXML로 만든다 - 구현은 Certificate 프로젝트에 둔다.
public interface IShippedHistoryExporter
{
    // 지정 경로에 출하 이력 xlsx를 만든다(기간·항목 포함).
    void Export(string filePath, DateTime dateFrom, DateTime dateTo, IReadOnlyList<TatLotItemDto> items);
}
