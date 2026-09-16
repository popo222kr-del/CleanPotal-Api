using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Data;

// 감사 로그를 남기는 구현. 여기서는 DbContext에 추가만 하고 저장은 하지 않는다 - 호출한 서비스가
// SaveChanges를 부를 때 업무 데이터와 같은 트랜잭션으로 함께 커밋된다. 업무는 성공했는데 로그만
// 빠지거나 그 반대인 상황을 막기 위한 의도적인 설계다.
public class EfAuditLogger : IAuditLogger
{
    private readonly ApplicationDbContext _context;

    public EfAuditLogger(ApplicationDbContext context)
    {
        _context = context;
    }

    public void Log(string action, string entityName, string entityId, string actor, string? detail = null)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            OccurredAt = DateTime.Now,
            Actor = actor,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail
        });
    }
}
