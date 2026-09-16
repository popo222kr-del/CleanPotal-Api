using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Infrastructure.Data;
using System.Linq.Expressions;

namespace ProductionManagement.Infrastructure.Repositories;

// 모든 엔티티가 공유하는 기본 저장소. 단순 CRUD만 담당하고 업무 규칙은 담지 않는다.
// 열린 제네릭으로 한 번만 등록하므로(DependencyInjection.cs) 엔티티마다 저장소 클래스를 만들 필요가 없다.
// 조인·집계가 필요한 조회는 이 저장소가 아니라 전용 쿼리 저장소(EfLotQueryRepository 등)가 맡는다.
public class EfRepository<TEntity, TId> : IRepository<TEntity, TId> where TEntity : Entity<TId>
{
    private readonly ApplicationDbContext _context;

    public EfRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
        => await _context.Set<TEntity>().FindAsync(new object?[] { id! }, cancellationToken);

    public async Task<IReadOnlyList<TEntity>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _context.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await _context.Set<TEntity>().AsNoTracking().AnyAsync(predicate, cancellationToken);

    // ListAllAsync/ExistsAsync와 달리 여기는 일부러 AsNoTracking을 붙이지 않는다: 같은 DbContext(=이 앱은
    // 세션 전체에서 하나를 재사용) 안에서 이미 추적 중인 엔티티를 다시 이 메서드로 조회한 뒤 Update()를
    // 부르면, AsNoTracking으로 만든 새 인스턴스와 기존 추적 인스턴스가 같은 Id로 충돌해 예외가 난다
    // (OperActionServiceTests에서 실제로 재현됨). Tracking 상태로 가져오면 이미 추적 중이던 인스턴스를
    // 그대로 재사용하게 되어 이 문제가 없다.
    public async Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await _context.Set<TEntity>().Where(predicate).ToListAsync(cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await _context.Set<TEntity>().AddAsync(entity, cancellationToken);

    public void Update(TEntity entity)
        => _context.Set<TEntity>().Update(entity);

    public void Remove(TEntity entity)
        => _context.Set<TEntity>().Remove(entity);
}
