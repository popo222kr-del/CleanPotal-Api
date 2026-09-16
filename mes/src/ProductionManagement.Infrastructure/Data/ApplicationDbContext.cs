using Microsoft.EntityFrameworkCore;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data;

// EF Core DbContext - 이 앱의 모든 테이블 목록이자 DB 출입구.
// 여기 DbSet으로 선언된 것만 EF가 테이블로 인식한다. 컬럼 타입·인덱스·관계 같은 세부 매핑은 이
// 파일이 아니라 Configurations 폴더의 IEntityTypeConfiguration 클래스들이 담당하고,
// OnModelCreating이 어셈블리를 훑어 한꺼번에 적용한다.
// DB는 포털(CleanPotal)과 같은 SQL Server 를 쓰고, 테이블에는 Mes 접두사가 붙는다.
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<ProcessDefinition> ProcessDefinitions => Set<ProcessDefinition>();
    public DbSet<ProcessRoute> ProcessRoutes => Set<ProcessRoute>();
    public DbSet<ProcessRouteStep> ProcessRouteSteps => Set<ProcessRouteStep>();
    public DbSet<SystemSequence> SystemSequences => Set<SystemSequence>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<ProcessHistory> ProcessHistories => Set<ProcessHistory>();
    public DbSet<QuantityTransaction> QuantityTransactions => Set<QuantityTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Hold> Holds => Set<Hold>();
    public DbSet<Rework> Reworks => Set<Rework>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<ProcessTransitionDefinition> ProcessTransitionDefinitions => Set<ProcessTransitionDefinition>();
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();
    public DbSet<ProductProcessFlow> ProductProcessFlows => Set<ProductProcessFlow>();
    public DbSet<LineDefinition> LineDefinitions => Set<LineDefinition>();
    public DbSet<RecipeDefinition> RecipeDefinitions => Set<RecipeDefinition>();
    public DbSet<ParameterDefinition> ParameterDefinitions => Set<ParameterDefinition>();
    public DbSet<ProductRecipeAssignment> ProductRecipeAssignments => Set<ProductRecipeAssignment>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<ProductParameterAssignment> ProductParameterAssignments => Set<ProductParameterAssignment>();
    public DbSet<InspectionRecord> InspectionRecords => Set<InspectionRecord>();

    /// <summary>
    /// MES 테이블에 붙는 접두사. 포털(CleanPotal)과 <b>같은 DB</b>를 쓰기 때문에 필요하다.
    /// 접두사가 없으면 Users·InspectionRecords 가 포털 테이블과 그대로 부딪힌다.
    /// 앞으로 포털에 테이블이 늘어도 부딪히지 않도록 일부만이 아니라 전부에 붙인다.
    /// </summary>
    public const string TablePrefix = "Mes";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // 설정 클래스들이 ToTable 로 정한 이름 위에 접두사를 덧붙인다(설정을 일일이 고치지 않는다).
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null || table.StartsWith(TablePrefix, StringComparison.Ordinal)) continue;
            entity.SetTableName(TablePrefix + table);
        }
    }

    public override int SaveChanges()
    {
        BumpLotConcurrencyVersions();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpLotConcurrencyVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    // 동시 수정 충돌은 애플리케이션이 Lot.Version 을 직접 올려 감지한다.
    // (SQL Server 의 ROWVERSION 에 기대지 않는다 — SQLite 개발 환경에서도 같게 동작해야 한다.)
    private void BumpLotConcurrencyVersions()
    {
        foreach (var entry in ChangeTracker.Entries<Lot>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version++;
            }
        }
    }
}
