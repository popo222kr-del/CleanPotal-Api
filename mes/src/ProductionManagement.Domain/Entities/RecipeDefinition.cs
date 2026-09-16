namespace ProductionManagement.Domain.Entities;

// "레시피 정의" 시트 Master Data(세정/건조/열처리 배합·시간 등). 제품별로 공정(세정/건조/Laser&CO2/Bake)
// 마다 다른 레시피가 배정될 수 있어 ProductRecipeAssignment로 (제품, 공정) 쌍마다 하나씩 연결한다.
public class RecipeDefinition : Entity<int>
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // 이 레시피가 속한 공정(3000=세정/4000=건조/4100=Laser&CO2/5000=Bake) - "레시피 정의" 시트의 OPER
    // 컬럼 값 그대로. 제품 레시피 드롭다운에서 해당 OPER의 레시피만 골라 보여주는 데 쓴다(2026-08-24 피드백).
    public int OperCode { get; set; }

    // "레시피 정의" 시트의 READ TIME (Min) 컬럼 - 해당 레시피 진행 소요 시간(분). 없는 레시피도 있어 nullable.
    public int? ReadTimeMinutes { get; set; }

    public bool IsActive { get; set; } = true;
}
