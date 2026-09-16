using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 제품별 검사 파라미터 배정 테이블 매핑.
// 제품+파라미터 조합이 유일하다(같은 항목을 두 번 배정할 수 없다).
public class ProductParameterAssignmentConfiguration : IEntityTypeConfiguration<ProductParameterAssignment>
{
    public void Configure(EntityTypeBuilder<ProductParameterAssignment> builder)
    {
        builder.HasIndex(a => new { a.ProductId, a.ParameterDefinitionId }).IsUnique();

        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.ParameterDefinition).WithMany().HasForeignKey(a => a.ParameterDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
