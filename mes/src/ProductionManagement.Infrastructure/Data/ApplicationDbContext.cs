using Microsoft.EntityFrameworkCore;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data;

// EF Core DbContext - 이 앱의 모든 테이블 목록이자 DB 출입구.
// 여기 DbSet으로 선언된 것만 EF가 테이블로 인식한다. 컬럼 타입·인덱스·관계 같은 세부 매핑은 이
// 파일이 아니라 Configurations 폴더의 IEntityTypeConfiguration 클래스들이 담당하고,
// OnModelCreating이 어셈블리를 훑어 한꺼번에 적용한다.
// DB는 SQLite 파일 하나이며 앱 시작 때 MigrateAsync가 스키마를 최신으로 맞춘다.
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
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

    // SQLite에는 SQL Server의 ROWVERSION 같은 자동 증가 concurrency token이 없어서,
    // 저장 시점에 애플리케이션이 직접 Lot.Version을 증가시켜 동시 수정 충돌을 감지한다 (CLAUDE.md 8번).
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
