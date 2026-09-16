using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] TRAN 코드(공정 전이 규칙) 마스터 테이블 매핑.
// TranId가 유일하고, "이 공정에서 지금 쓸 수 있는 TRAN"을 뽑을 때 (출발 OPER, 사용여부)로 찾는다.
public class ProcessTransitionDefinitionConfiguration : IEntityTypeConfiguration<ProcessTransitionDefinition>
{
    public void Configure(EntityTypeBuilder<ProcessTransitionDefinition> builder)
    {
        builder.Property(t => t.TranId).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(50).IsRequired();
        builder.Property(t => t.SourceOperCode).IsRequired();
        builder.Property(t => t.TranCode).IsRequired();

        builder.HasIndex(t => t.TranId).IsUnique();
        builder.HasIndex(t => new { t.SourceOperCode, t.IsActive });
    }
}
