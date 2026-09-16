using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] HOLD(작업 보류) 이력 테이블 매핑.
// LotId로 찾고, IsReleased로 "아직 안 풀린 HOLD"를 걸러내므로 둘 다 인덱스를 둔다.
public class HoldConfiguration : IEntityTypeConfiguration<Hold>
{
    public void Configure(EntityTypeBuilder<Hold> builder)
    {
        builder.Property(h => h.RaisedBy).HasMaxLength(100).IsRequired();
        builder.Property(h => h.Reason).HasMaxLength(500).IsRequired();
        builder.Property(h => h.Remarks).HasMaxLength(500);
        builder.Property(h => h.ReleasedBy).HasMaxLength(100);
        builder.Property(h => h.ApprovedBy).HasMaxLength(100);
        builder.Property(h => h.ReleaseReason).HasMaxLength(500);
        builder.Property(h => h.ActionTaken).HasMaxLength(500);

        builder.HasOne(h => h.Lot).WithMany().HasForeignKey(h => h.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ProcessDefinition).WithMany().HasForeignKey(h => h.ProcessDefinitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.LotId);
        builder.HasIndex(h => h.IsReleased);
    }
}
