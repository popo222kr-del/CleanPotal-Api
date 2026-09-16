using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 제품별·공정별 레시피 배정 테이블 매핑.
// 제품+공정+레시피 조합이 유일하다 - 한 공정에 레시피 여러 개를 달 수 있고 그중 하나가 MAIN이다.
public class ProductRecipeAssignmentConfiguration : IEntityTypeConfiguration<ProductRecipeAssignment>
{
    public void Configure(EntityTypeBuilder<ProductRecipeAssignment> builder)
    {
        // 2026-08-26: 한 공정에 여러 레시피를 둘 수 있게 (Product, Process) 유일 제약을 (Product, Process,
        // Recipe) 유일로 바꿨다(같은 레시피 중복만 막는다).
        builder.HasIndex(a => new { a.ProductId, a.ProcessDefinitionId, a.RecipeDefinitionId }).IsUnique();

        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.ProcessDefinition).WithMany().HasForeignKey(a => a.ProcessDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.RecipeDefinition).WithMany().HasForeignKey(a => a.RecipeDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
