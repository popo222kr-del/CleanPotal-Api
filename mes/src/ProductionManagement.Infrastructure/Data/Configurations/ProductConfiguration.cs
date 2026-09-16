using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 제품 마스터 테이블 매핑.
// 유일 키는 CleaningCode(세정코드) 하나뿐이다. 품목코드·규격·단가는 검색용 비고유 인덱스다.
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.ProductCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ItemCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ProductName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.SerialNumber).HasMaxLength(100);
        builder.Property(p => p.CleaningCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ItemCategory).HasMaxLength(50);

        // 2026-08-21 "제품 규격"으로 전환하며 Unique 제거 - 서로 다른 제품이 같은 규격을 가질 수 있다.
        // 검색 대상이라 인덱스 자체는(비고유로) 유지한다.
        builder.HasIndex(p => p.ProductCode);
        builder.HasIndex(p => p.ItemCode);
        builder.HasIndex(p => p.SerialNumber);
        builder.HasIndex(p => p.CleaningCode).IsUnique();

        builder.HasOne(p => p.Customer)
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
