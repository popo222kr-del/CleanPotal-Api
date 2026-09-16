using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 전산등록(고객이 맡긴 물량을 시스템에 올리는 행위) 테이블 매핑.
// 반출번호가 유일하다 - 같은 반출번호로 두 번 등록할 수 없다.
public class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.Property(r => r.ExportNumber).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Line).HasMaxLength(50).IsRequired();
        builder.Property(r => r.ProcessLabel).HasMaxLength(50).IsRequired();
        builder.Property(r => r.RegisteredBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.PmEquipmentName).HasMaxLength(50).IsRequired();
        builder.Property(r => r.TeamName).HasMaxLength(50).IsRequired();
        builder.Property(r => r.OrderNumber).HasMaxLength(50).IsRequired();

        builder.HasIndex(r => r.ExportNumber).IsUnique();

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Product)
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ProcessRoute)
            .WithMany()
            .HasForeignKey(r => r.ProcessRouteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
