using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] LINE(업체 대분류) 마스터 테이블 매핑.
// LINE 코드가 유일하다.
public class LineDefinitionConfiguration : IEntityTypeConfiguration<LineDefinition>
{
    public void Configure(EntityTypeBuilder<LineDefinition> builder)
    {
        builder.Property(l => l.Code).HasMaxLength(20).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(50).IsRequired();
        builder.Property(l => l.UserCode).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => l.Code).IsUnique();
    }
}
