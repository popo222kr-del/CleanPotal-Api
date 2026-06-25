using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// EF Core DbContext. 기존 WPF의 DatabaseHelper(원시 SQL) + AuthDatabaseHelper(users.json)를
/// 하나의 RDBMS(SQLite/PostgreSQL)로 통합한다.
/// </summary>
public class CleanPotalDbContext : DbContext
{
    public CleanPotalDbContext(DbContextOptions<CleanPotalDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ShiftSchedule> ShiftSchedules => Set<ShiftSchedule>();
    public DbSet<TeamEvent> TeamEvents => Set<TeamEvent>();
    public DbSet<Handover> Handovers => Set<Handover>();
    public DbSet<PortalGroup> PortalGroups => Set<PortalGroup>();
    public DbSet<PortalItem> PortalItems => Set<PortalItem>();
    public DbSet<ProdReq> ProdReqs => Set<ProdReq>();
    public DbSet<ProductionMeeting> ProductionMeetings => Set<ProductionMeeting>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
    public DbSet<BrokenRecord> BrokenRecords => Set<BrokenRecord>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).IsRequired();
            e.Property(u => u.RealName).IsRequired();
        });

        b.Entity<ShiftSchedule>(e =>
        {
            // 한 사람의 같은 날짜는 한 행만 — 도장 Upsert의 기준
            e.HasIndex(s => new { s.MemberName, s.TargetDate }).IsUnique();
        });

        b.Entity<TeamEvent>();
        b.Entity<Handover>();

        b.Entity<PortalGroup>(e =>
        {
            e.HasMany(g => g.Items).WithOne(i => i.Group!).HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PortalItem>();

        b.Entity<Quotation>(e =>
        {
            e.HasMany(q => q.Items).WithOne(i => i.Quotation!).HasForeignKey(i => i.QuotationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<QuotationItem>(e =>
        {
            e.Property(i => i.Quantity).HasColumnType("decimal(18,2)");
            e.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        });
    }
}
