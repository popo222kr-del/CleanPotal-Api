namespace ProductionManagement.Domain.Entities;

// 모든 엔티티의 공통 기반. Id의 타입을 열어 둔 이유는 int 말고 다른 키를 쓸 여지를 남기기 위해서지만
// 현재 이 프로젝트의 엔티티는 전부 Entity<int>다. setter가 protected라 Id는 EF와 엔티티 자신만 채운다
// (외부에서 실수로 키를 바꾸지 못하게 한다).
public abstract class Entity<TId>
{
    public TId Id { get; protected set; } = default!;
}
