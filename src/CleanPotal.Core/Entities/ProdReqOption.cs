namespace CleanPotal.Core.Entities;

/// <summary>생산팀 요청사항 등록 옵션 (구분/세부 위치/요청 분류) — 하드코딩 대신 관리 화면에서 편집.</summary>
public class ProdReqOption
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";     // category(구분) | subloc(세부 위치) | reqtype(요청 분류)
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";   // subloc일 때 상위 구분명
    public int OrderIndex { get; set; }
}
