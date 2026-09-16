using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 레시피 마스터 테이블 매핑.
// 레시피 코드가 유일하다.
public class RecipeDefinitionConfiguration : IEntityTypeConfiguration<RecipeDefinition>
{
    public void Configure(EntityTypeBuilder<RecipeDefinition> builder)
    {
        builder.Property(r => r.Code).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(100).IsRequired();

        builder.HasIndex(r => r.Code).IsUnique();
    }
}
