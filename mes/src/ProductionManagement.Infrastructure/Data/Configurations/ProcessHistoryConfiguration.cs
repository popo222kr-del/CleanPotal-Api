using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 공정 수행 이력(LOT이 어느 공정을 언제 통과했는지) 테이블 매핑.
// LOT 단위 조회가 전부라 LotId에만 인덱스를 둔다.
public class ProcessHistoryConfiguration : IEntityTypeConfiguration<ProcessHistory>
{
    public void Configure(EntityTypeBuilder<ProcessHistory> builder)
    {
        builder.Property(h => h.Worker).HasMaxLength(100).IsRequired();
        builder.Property(h => h.Remarks).HasMaxLength(500);

        builder.HasOne(h => h.Lot)
            .WithMany()
            .HasForeignKey(h => h.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.ProcessDefinition)
            .WithMany()
            .HasForeignKey(h => h.ProcessDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.LotId);
    }
}
