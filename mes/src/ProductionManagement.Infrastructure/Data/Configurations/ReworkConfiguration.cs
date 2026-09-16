using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 재작업 이력 테이블 매핑.
// LOT 단위 조회라 LotId에 인덱스를 둔다.
public class ReworkConfiguration : IEntityTypeConfiguration<Rework>
{
    public void Configure(EntityTypeBuilder<Rework> builder)
    {
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.DecidedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Remarks).HasMaxLength(500);

        builder.HasOne(r => r.Lot).WithMany().HasForeignKey(r => r.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.ProcessDefinition).WithMany().HasForeignKey(r => r.ProcessDefinitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.LotId);
    }
}
