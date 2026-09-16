namespace ProductionManagement.Application.Interfaces;

// 변경 사항을 언제 저장할지 서비스가 직접 정하게 해주는 장치.
// 여러 엔티티를 고친 뒤 SaveChangesAsync 한 번으로 묶어 "전부 되거나 전부 안 되게" 만든다.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
