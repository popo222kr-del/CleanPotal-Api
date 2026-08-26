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
    public DbSet<ProdReqOption> ProdReqOptions => Set<ProdReqOption>();
    public DbSet<ProdReqRead> ProdReqReads => Set<ProdReqRead>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();
    public DbSet<ScheduleRecipe> ScheduleRecipes => Set<ScheduleRecipe>();
    public DbSet<ScheduleEquipment> ScheduleEquipments => Set<ScheduleEquipment>();
    public DbSet<ProductionMeeting> ProductionMeetings => Set<ProductionMeeting>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();
    public DbSet<BrokenRecord> BrokenRecords => Set<BrokenRecord>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<MaterialRosterMember> MaterialRosterMembers => Set<MaterialRosterMember>();
    public DbSet<MaterialScheduleEntry> MaterialScheduleEntries => Set<MaterialScheduleEntry>();
    public DbSet<MaterialDayNote> MaterialDayNotes => Set<MaterialDayNote>();
    public DbSet<ProductMaster> ProductMasters => Set<ProductMaster>();
    public DbSet<GlobalTemplate> GlobalTemplates => Set<GlobalTemplate>();
    public DbSet<QuotationConfig> QuotationConfigs => Set<QuotationConfig>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportBlock> ReportBlocks => Set<ReportBlock>();
    public DbSet<BrokenTraining> BrokenTrainings => Set<BrokenTraining>();
    public DbSet<BrokenGoal> BrokenGoals => Set<BrokenGoal>();
    public DbSet<BrokenMeta> BrokenMetas => Set<BrokenMeta>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<EducationPlan> EducationPlans => Set<EducationPlan>();
    public DbSet<WorkMember> WorkMembers => Set<WorkMember>();
    public DbSet<WorkAccount> WorkAccounts => Set<WorkAccount>();
    public DbSet<WorkEdu> WorkEdus => Set<WorkEdu>();
    public DbSet<EquipmentAnalysis> EquipmentAnalyses => Set<EquipmentAnalysis>();
    public DbSet<EquipmentMaster> EquipmentMasters => Set<EquipmentMaster>();
    public DbSet<EquipmentCheckNote> EquipmentCheckNotes => Set<EquipmentCheckNote>();
    public DbSet<EquipmentActionLog> EquipmentActionLogs => Set<EquipmentActionLog>();
    public DbSet<UserAuditLog> UserAuditLogs => Set<UserAuditLog>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ── SQL Server 대비: 키/인덱스에 쓰이는 문자열 컬럼은 길이를 지정한다.
        //    (미지정 시 nvarchar(max) 가 되어 SQL Server 에서 인덱스/PK 로 못 씀.
        //     SQLite 는 길이 제한을 무시하므로 기존 동작에 영향 없음)
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).IsRequired().HasMaxLength(150);
            e.Property(u => u.RealName).IsRequired();
        });

        b.Entity<ShiftSchedule>(e =>
        {
            // 한 사람의 같은 날짜는 한 행만 — 도장 Upsert의 기준
            e.Property(s => s.MemberName).HasMaxLength(100);
            e.HasIndex(s => new { s.MemberName, s.TargetDate }).IsUnique();
        });

        b.Entity<TeamEvent>();
        b.Entity<Handover>();
        b.Entity<ProdReqRead>(e =>
        {
            e.HasKey(r => r.Username);
            e.Property(r => r.Username).HasMaxLength(150);
        });
        b.Entity<ScheduleBlock>(e =>
        {
            e.Property(x => x.BoardDate).HasMaxLength(30);
            e.HasIndex(x => x.BoardDate);
        });
        b.Entity<ScheduleRecipe>();
        b.Entity<ScheduleEquipment>();

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
            e.Property(i => i.ListPrice).HasColumnType("decimal(18,2)");
            e.Property(i => i.Qty).HasColumnType("decimal(18,2)");
        });
        b.Entity<InventorySnapshot>(e =>
        {
            e.Property(s => s.SnapshotDate).HasMaxLength(30);
            e.HasIndex(s => s.ItemId);
            e.HasIndex(s => s.SnapshotDate);
        });

        b.Entity<EquipmentAnalysis>(e =>
        {
            e.Property(a => a.ProcessType).HasMaxLength(100);
            e.Property(a => a.EqId).HasMaxLength(60);
            e.Property(a => a.BathGb).HasMaxLength(100);
            e.Property(a => a.Category).HasMaxLength(100);
            e.Property(a => a.AnalysisDate).HasMaxLength(30);
            // 중복 방지 키 (업로드 INSERT OR IGNORE)
            e.HasIndex(a => new { a.ProcessType, a.EqId, a.BathGb, a.Category, a.AnalysisDate }).IsUnique();
        });
        b.Entity<EquipmentMaster>(e =>
        {
            e.HasKey(m => m.EqId);
            e.Property(m => m.EqId).HasMaxLength(60);
        });
        b.Entity<EquipmentCheckNote>(e =>
        {
            e.HasKey(n => new { n.EqId, n.CheckDate });
            e.Property(n => n.EqId).HasMaxLength(60);
            e.Property(n => n.CheckDate).HasMaxLength(30);
        });

        b.Entity<MaterialRosterMember>();
        b.Entity<MaterialScheduleEntry>(e =>
        {
            e.Property(s => s.PersonName).HasMaxLength(100);
            e.Property(s => s.Period).HasMaxLength(10);
            // 하루의 (담당자 × 오전/오후)는 한 칸만 — 저장 시 교체 기준
            e.HasIndex(s => new { s.TargetDate, s.PersonName, s.Period }).IsUnique();
        });
        b.Entity<MaterialDayNote>(e =>
        {
            e.HasIndex(n => n.TargetDate).IsUnique();
        });

        b.Entity<ProductMaster>(e =>
        {
            e.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");
        });
        b.Entity<GlobalTemplate>();
        b.Entity<QuotationConfig>();
        b.Entity<Recipe>();

        b.Entity<Report>(e =>
        {
            e.HasMany(r => r.Blocks).WithOne(x => x.Report!).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReportBlock>();

        b.Entity<BrokenTraining>();
        b.Entity<BrokenGoal>();
        b.Entity<BrokenMeta>();
        b.Entity<Notice>();
        b.Entity<Dispatch>();
        b.Entity<EducationPlan>();
        b.Entity<WorkMember>();
        b.Entity<WorkAccount>();
        b.Entity<WorkEdu>();
    }
}
