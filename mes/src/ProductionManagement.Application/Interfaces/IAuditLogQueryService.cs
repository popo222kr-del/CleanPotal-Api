using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 감사 로그 조회. 세정 이력 조회 화면의 "감사 로그(Audit Log)" 탭이 쓴다.
// 쓰기는 없다 - 기록은 각 서비스가 IAuditLogger로 남기고 여기서는 읽기만 한다.
public interface IAuditLogQueryService
{
    Task<IReadOnlyList<AuditLogDto>> SearchAsync(AuditLogSearchRequest request, CancellationToken cancellationToken = default);
}
