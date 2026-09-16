namespace ProductionManagement.Domain.Entities;

// 제품 하나가 특정 공정(세정/건조/Laser&CO2/Bake - 작업시작이 필요한 4개 OPER)에서 쓰는 레시피.
// 2026-08-26 피드백: 한 공정(OPER)에 여러 종류의 레시피를 설정할 수 있게 확장했다. 그중 하나를
// MAIN(IsMain)으로 지정하면 OPER 화면 RECIPE ID 드롭다운의 기본값이 된다. USE FLAG(IsActive)로
// 사용/미사용을, MIN/MAX로 이 레시피의 스펙 범위를 함께 둔다.
public class ProductRecipeAssignment : Entity<int>
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ProcessDefinitionId { get; set; }
    public ProcessDefinition ProcessDefinition { get; set; } = null!;

    public int RecipeDefinitionId { get; set; }
    public RecipeDefinition RecipeDefinition { get; set; } = null!;

    public bool IsMain { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
}
