using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 제품 단가 이력 테이블 매핑.
// (제품, 적용일) 인덱스로 특정 시점의 단가를 찾는다 - 단가는 덮어쓰지 않고 이력으로 쌓인다.
public class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");
        builder.HasIndex(p => new { p.ProductId, p.EffectiveDate });
        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
