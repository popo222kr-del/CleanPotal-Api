using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 채번기(SystemSequence)와 시스템 설정(SystemSetting) 테이블 매핑.
// 이름/키가 각각 유일하다. LOT 번호는 이 채번기를 통해서만 발급된다.
public class SystemSequenceConfiguration : IEntityTypeConfiguration<SystemSequence>
{
    public void Configure(EntityTypeBuilder<SystemSequence> builder)
    {
        builder.Property(s => s.SequenceName).HasMaxLength(50).IsRequired();
        builder.HasIndex(s => s.SequenceName).IsUnique();
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.Property(s => s.SettingKey).HasMaxLength(100).IsRequired();
        builder.Property(s => s.SettingValue).HasMaxLength(500).IsRequired();
        builder.HasIndex(s => s.SettingKey).IsUnique();
    }
}
