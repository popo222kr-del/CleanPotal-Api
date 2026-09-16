using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 매출 고객사(업체) 테이블 매핑.
// 업체코드만 유일하다. 반출 접두어는 여러 업체가 공유할 수 있어 유일 제약이 없다.
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.CustomerCode).HasMaxLength(30).IsRequired();
        builder.Property(c => c.CustomerName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ExportPrefix).HasMaxLength(10).IsRequired();

        builder.HasIndex(c => c.CustomerCode).IsUnique();
        // 2026-08-28: 반출 접두어는 여러 고객사가 공유할 수 있다(LINE 단위 등). 반출번호는 전산등록에서
        // 작업자가 수기 입력하므로 접두어의 유일성/자동부여가 필요 없어 UNIQUE 제약을 제거한다.
    }
}
