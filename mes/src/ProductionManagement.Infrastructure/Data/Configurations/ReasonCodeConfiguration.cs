using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 사유 코드(HOLD/재작업/SKIP 등에서 고르는 이유) 마스터 테이블 매핑.
// 분류+코드 조합이 유일하다 - 분류가 다르면 같은 코드를 다시 쓸 수 있다.
public class ReasonCodeConfiguration : IEntityTypeConfiguration<ReasonCode>
{
    public void Configure(EntityTypeBuilder<ReasonCode> builder)
    {
        builder.Property(r => r.Category).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(50).IsRequired();

        builder.HasIndex(r => new { r.Category, r.Code }).IsUnique();
    }
}
