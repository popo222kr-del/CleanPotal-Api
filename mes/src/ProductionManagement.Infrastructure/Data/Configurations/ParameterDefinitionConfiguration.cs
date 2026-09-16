using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 검사 파라미터(측정 항목) 마스터 테이블 매핑.
// 제품+코드+공정 조합이 유일하다 - 같은 항목이라도 공정이 다르면 별개로 잡는다.
public class ParameterDefinitionConfiguration : IEntityTypeConfiguration<ParameterDefinition>
{
    public void Configure(EntityTypeBuilder<ParameterDefinition> builder)
    {
        builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Oper).HasMaxLength(20);
        builder.Property(p => p.Unit).HasMaxLength(20);
        builder.Property(p => p.CertificateLabel).HasMaxLength(50);

        // 2026-08-26: 파라미터를 제품별로 관리하도록 바꾸면서 (ProductId, Code, Oper) 복합 유일로 확장했다.
        // 서로 다른 제품이 같은 코드+OPER를 각자 가질 수 있어야 하기 때문이다.
        builder.HasIndex(p => new { p.ProductId, p.Code, p.Oper }).IsUnique();
    }
}
