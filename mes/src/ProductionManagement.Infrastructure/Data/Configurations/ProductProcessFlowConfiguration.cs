using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 제품에 어떤 공정 플로우를 쓸지 배정하는 테이블 매핑.
// 제품+플로우 조합이 유일하다.
public class ProductProcessFlowConfiguration : IEntityTypeConfiguration<ProductProcessFlow>
{
    public void Configure(EntityTypeBuilder<ProductProcessFlow> builder)
    {
        builder.HasIndex(f => new { f.ProductId, f.ProcessRouteId }).IsUnique();

        builder.HasOne(f => f.Product).WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.ProcessRoute).WithMany().HasForeignKey(f => f.ProcessRouteId).OnDelete(DeleteBehavior.Cascade);
    }
}
