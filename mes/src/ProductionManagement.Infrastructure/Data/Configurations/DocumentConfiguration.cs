using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data.Configurations;

// EF Core가 어셈블리를 훑어 자동으로 찾아 적용한다(ApplicationDbContext.OnModelCreating의
// ApplyConfigurationsFromAssembly). 그래서 이 클래스를 직접 참조하는 코드는 어디에도 없다 -
// 파일을 만들어 두는 것만으로 매핑에 반영된다.
// [EF 매핑] 성적서 등 LOT에 딸린 문서 파일 테이블 매핑.
// 항상 LOT 단위로 찾으므로 LotId에 인덱스를 둔다.
public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.Property(d => d.FileName).HasMaxLength(260).IsRequired();
        builder.Property(d => d.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(d => d.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasOne(d => d.Lot).WithMany().HasForeignKey(d => d.LotId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.LotId);
    }
}
