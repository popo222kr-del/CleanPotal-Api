using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 공정 플로우(ProcessRoute)와 그 단계(ProcessRouteStep) 테이블 매핑.
// 플로우 코드가 유일하고, 한 플로우 안에서 단계 순번이 겹치지 않도록 (플로우, 순번) 조합도 유일하다.
public class ProcessRouteConfiguration : IEntityTypeConfiguration<ProcessRoute>
{
    public void Configure(EntityTypeBuilder<ProcessRoute> builder)
    {
        builder.Property(r => r.RouteCode).HasMaxLength(30).IsRequired();
        builder.Property(r => r.RouteName).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.RouteCode).IsUnique();
    }
}

public class ProcessRouteStepConfiguration : IEntityTypeConfiguration<ProcessRouteStep>
{
    public void Configure(EntityTypeBuilder<ProcessRouteStep> builder)
    {
        builder.HasOne(s => s.ProcessRoute)
            .WithMany(r => r.Steps)
            .HasForeignKey(s => s.ProcessRouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.ProcessDefinition)
            .WithMany()
            .HasForeignKey(s => s.ProcessDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.ProcessRouteId, s.StepOrder }).IsUnique();
    }
}
