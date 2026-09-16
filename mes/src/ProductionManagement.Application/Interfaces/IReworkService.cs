using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 재작업 이력 조회. "공정관리 > 재작업 관리" 화면이 쓴다.
// 재작업 지시 자체는 OPER 화면의 TRAN 실행에서 일어나고 여기서는 결과만 본다.
public interface IReworkService
{
    Task<IReadOnlyList<ReworkDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
