using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 공정 마스터(입고/세정/출고검사 등) 테이블 매핑.
// 공정코드와 OPER 번호가 각각 유일하다. OPER 번호(2000/3000/7000...)가 화면·로직에서 실제 키로 쓰인다.
public class ProcessDefinitionConfiguration : IEntityTypeConfiguration<ProcessDefinition>
{
    public void Configure(EntityTypeBuilder<ProcessDefinition> builder)
    {
        builder.Property(p => p.ProcessCode).HasMaxLength(30).IsRequired();
        builder.Property(p => p.ProcessName).HasMaxLength(50).IsRequired();
        builder.Property(p => p.OperCode).IsRequired();

        builder.HasIndex(p => p.ProcessCode).IsUnique();
        builder.HasIndex(p => p.OperCode).IsUnique();
    }
}
