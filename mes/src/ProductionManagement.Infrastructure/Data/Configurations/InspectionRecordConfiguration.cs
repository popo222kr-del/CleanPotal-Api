using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 검사 실측값 테이블 매핑.
// LOT+공정+파라미터 조합이 유일하다 - 같은 항목을 두 번 기록하면 덮어쓴다는 뜻이다.
public class InspectionRecordConfiguration : IEntityTypeConfiguration<InspectionRecord>
{
    public void Configure(EntityTypeBuilder<InspectionRecord> builder)
    {
        builder.HasIndex(r => new { r.LotId, r.ProcessDefinitionId, r.ParameterDefinitionId }).IsUnique();

        builder.HasOne(r => r.Lot).WithMany().HasForeignKey(r => r.LotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.ProcessDefinition).WithMany().HasForeignKey(r => r.ProcessDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.ParameterDefinition).WithMany().HasForeignKey(r => r.ParameterDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
