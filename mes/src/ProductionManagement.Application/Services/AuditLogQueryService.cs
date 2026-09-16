using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Application.Services;

// 감사 로그 조회 구현. 키워드·기간으로 걸러 최근 것부터 돌려준다.
// 로그를 "남기는" 쪽은 IAuditLogger이고 여기는 읽기 전용이다.
public class AuditLogQueryService : IAuditLogQueryService
{
    private readonly IRepository<AuditLog, int> _auditLogs;

    public AuditLogQueryService(IRepository<AuditLog, int> auditLogs)
    {
        _auditLogs = auditLogs;
    }

    public async Task<IReadOnlyList<AuditLogDto>> SearchAsync(AuditLogSearchRequest request, CancellationToken cancellationToken = default)
    {
        var dateFrom = request.DateFrom ?? DateTime.MinValue;
        var dateTo = request.DateTo?.Date.AddDays(1) ?? DateTime.MaxValue;

        var logs = await _auditLogs.ListAsync(
            a => a.OccurredAt >= dateFrom && a.OccurredAt < dateTo,
            cancellationToken);

        var query = logs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(a =>
                a.Actor.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                a.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                a.EntityId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (a.Detail is not null && a.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(a => a.OccurredAt)
            .Take(request.Take)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.Actor, a.Action, a.EntityName, a.EntityId, a.Detail))
            .ToList();
    }
}
