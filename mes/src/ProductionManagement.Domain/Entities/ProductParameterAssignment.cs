namespace ProductionManagement.Domain.Entities;

// 이 제품에서 실제로 검사/기록하는 파라미터 목록(다대다).
public class ProductParameterAssignment : Entity<int>
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ParameterDefinitionId { get; set; }
    public ParameterDefinition ParameterDefinition { get; set; } = null!;
}
