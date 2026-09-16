using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 사용자 계정 테이블 매핑.
// 로그인 아이디가 유일하다.
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // 2026-08-28: 로그인 아이디/비밀번호 계정으로 전환. LoginId가 유일 식별자다.
        builder.Property(u => u.LoginId).HasMaxLength(50).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(50);
        builder.Property(u => u.WindowsAccount).HasMaxLength(100); // 레거시(참고용), 더 이상 필수/유일 아님

        builder.HasIndex(u => u.LoginId).IsUnique();
    }
}
