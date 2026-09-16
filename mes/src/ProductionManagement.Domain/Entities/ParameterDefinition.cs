using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// "파라미터 정의" 시트 Master Data(검사 측정 항목: 외관/치수/두께 등). 제품마다 실제로 검사하는 항목이
// 달라 ProductParameterAssignment로 제품↔파라미터를 다대다로 연결한다.
public class ParameterDefinition : Entity<int>
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // ParameterType.Numeric일 때만 의미가 있다 - 엑셀 원본에는 값이 없어 전부 null로 시작하고,
    // IN INSP/FI INSP 화면에서 기준치를 참고용으로 보여줄 자리만 마련해둔다.
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }

    // 2026-08-25: 제품 셋업 "파라미터(검사 항목)" 표를 참조 이미지처럼(OPER/VALUE COUNT/UNIT까지) 다루기
    // 위해 추가. Oper = 이 파라미터를 측정하는 공정 구분("2100"/"7000"/"RCV" 등, 숫자 OPER 외 RCV 같은
    // 텍스트도 있어 문자열). ValueCount = 기록하는 값 개수(0/1). Unit = 단위(예: "g"). 같은 코드라도
    // OPER가 다르면 별개 행이라 (Code, Oper) 복합 유일이다(ParameterDefinitionConfiguration).
    public string? Oper { get; set; }
    public int ValueCount { get; set; }
    public string? Unit { get; set; }

    // 2026-08-26: 파라미터를 전역 마스터가 아니라 "제품별"로 관리하도록 전환(피드백: "파라미터는 기본적으로는
    // 아무 데이터 없는 상태로... 제품별로 사용 할 파라미터를 추가"). ProductId가 있는 행만 해당 제품의
    // 검사 항목이다. (ProductId, Code, Oper) 복합 유일 - 같은 제품 안에서 코드+OPER가 겹치지 않게 한다.
    public int? ProductId { get; set; }

    // 2026-08-27: 성적서 표기명. 앱 파라미터 코드(예: CHIP)와 성적서 양식의 항목명(예: Chipping)이 달라도,
    // 이 값으로 성적서 항목명과 매칭한다(마스터 데이터에 등록해 참조). 비어 있으면 Code로 매칭한다.
    public string? CertificateLabel { get; set; }
}
