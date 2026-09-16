using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] LOT 테이블 매핑 - 이 시스템의 중심 테이블.
// LotNumber는 유일(채번으로만 발급), S/N과 대표LOT은 조회용 인덱스. 외래키는 전부 Restrict라
// 제품·공정·플로우·전산등록은 그것을 참조하는 LOT이 남아 있는 한 지워지지 않는다.
public class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.Property(l => l.LotNumber).HasMaxLength(10).IsRequired();
        builder.Property(l => l.SerialNumber).HasMaxLength(50).IsRequired();
        builder.Property(l => l.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(l => l.UpdatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(l => l.LotNumber).IsUnique();
        builder.HasIndex(l => l.SerialNumber);
        builder.HasIndex(l => l.RepresentativeLotId);

        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.CurrentProcessDefinition)
            .WithMany()
            .HasForeignKey(l => l.CurrentProcessDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ProcessRoute)
            .WithMany()
            .HasForeignKey(l => l.ProcessRouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Registration)
            .WithMany()
            .HasForeignKey(l => l.RegistrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.RepresentativeLot)
            .WithMany()
            .HasForeignKey(l => l.RepresentativeLotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(l => l.Version).IsConcurrencyToken();
    }
}
