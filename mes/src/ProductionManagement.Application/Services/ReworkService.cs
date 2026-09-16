using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Application.Services;

// 재작업 이력 조회 구현. 재작업 지시는 OPER 화면의 TRAN 실행에서 일어나고 여기서는 결과만 읽는다.
public class ReworkService : IReworkService
{
    private readonly IRepository<Rework, int> _reworks;
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;

    public ReworkService(
        IRepository<Rework, int> reworks,
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<ProcessDefinition, int> processDefinitions)
    {
        _reworks = reworks;
        _lots = lots;
        _products = products;
        _processDefinitions = processDefinitions;
    }

    public async Task<IReadOnlyList<ReworkDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var reworks = await _reworks.ListAllAsync(cancellationToken);
        var lots = (await _lots.ListAllAsync(cancellationToken)).ToDictionary(l => l.Id);
        var products = (await _products.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var processes = (await _processDefinitions.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);

        return reworks
            .OrderByDescending(r => r.DecidedAt)
            .Select(r =>
            {
                lots.TryGetValue(r.LotId, out var lot);
                var productName = lot is not null && products.TryGetValue(lot.ProductId, out var product) ? product.ProductName : "-";
                var processName = processes.TryGetValue(r.ProcessDefinitionId, out var process) ? process.ProcessName : "-";

                return new ReworkDto(
                    r.Id,
                    r.LotId,
                    lot?.LotNumber ?? "-",
                    productName,
                    processName,
                    r.AttemptNumber,
                    r.Reason,
                    r.DecidedBy,
                    r.DecidedAt,
                    r.Remarks);
            })
            .ToList();
    }
}
