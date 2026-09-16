using System.Linq.Expressions;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Application.Interfaces;

// Remove()가 존재한다고 해서 모든 곳에서 물리 삭제를 써도 되는 것은 아니다.
// ProcessHistory 등 이력성 Entity는 Remove()를 호출하는 서비스 메서드를 만들지 않는다 (CLAUDE.md 절대 금지사항 참고).
public interface IRepository<TEntity, TId> where TEntity : Entity<TId>
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> ListAllAsync(CancellationToken cancellationToken = default);

    // 코드 중복 확인 등 단순 조건 체크용. 목록 전체를 Memory로 가져와 LINQ로 거르지 않기 위함 (CLAUDE.md 성능 원칙).
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    // 특정 Lot의 ProcessHistory처럼 "이 조건에 맞는 것만" DB에서 걸러 가져올 때 사용 (전체 조회 후 LINQ 금지).
    Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
