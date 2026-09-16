namespace ProductionManagement.Application.Interfaces;

// SaveChanges를 직접 호출하지 않는다. 호출한 Service가 본 업무 변경과 같은 트랜잭션(SaveChangesAsync)으로
// 함께 커밋해야 "업무 데이터는 저장됐는데 감사로그는 안 남는" 상황이 생기지 않는다.
public interface IAuditLogger
{
    void Log(string action, string entityName, string entityId, string actor, string? detail = null);
}
