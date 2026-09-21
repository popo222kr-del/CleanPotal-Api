namespace CleanPotal.Core.Entities;

/// <summary>
/// BROKEN 등록 칸의 드롭다운 목록. 제품군·발생단계처럼 다른 곳에 마스터가 없는 항목만 여기에 둔다.
/// 팀은 조직 관리, 유발자는 사용자 목록, 라인은 MES 라인에서 가져오므로 여기 담지 않는다.
/// </summary>
public class BrokenOption
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";   // productType(제품군) | occurStage(발생단계)
    public string Name { get; set; } = "";
    public int OrderIndex { get; set; }
}
