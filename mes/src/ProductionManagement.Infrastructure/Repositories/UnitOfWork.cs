using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure.Data;

namespace ProductionManagement.Infrastructure.Repositories;

// 한 화면에서 일어난 변경을 한 번에 저장하는 곳. 저장 시점을 서비스가 직접 정하게 해서, 여러 엔티티를
// 고친 뒤 "전부 되거나 전부 안 되게" 묶을 수 있다.
// 동시성 충돌(다른 사람이 먼저 같은 LOT을 처리한 경우)은 EF 예외를 그대로 흘려보내지 않고 사용자에게
// 보여줄 수 있는 문장으로 바꿔 던진다.
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("이미 다른 사용자가 처리한 Lot입니다. 최신 상태로 다시 조회한 뒤 시도해 주세요.");
        }
    }
}
