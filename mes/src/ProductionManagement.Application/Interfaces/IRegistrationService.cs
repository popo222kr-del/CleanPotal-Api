using ProductionManagement.Application.DTOs;

namespace ProductionManagement.Application.Interfaces;

// 전산등록 - 고객이 맡긴 물량을 시스템에 올려 LOT을 만드는 입구.
// 여기서 LOT이 태어나고 그때부터 공정이 시작된다. 엑셀에서 붙여넣은 여러 행을 한 번에 처리하는
// CreateManyAsync는 행마다 독립이라, 한 행이 실패해도 나머지는 그대로 등록된다.
public interface IRegistrationService
{
    Task<RegistrationResultDto> CreateAsync(RegistrationCreateRequest request, CancellationToken cancellationToken = default);

    // 한 번에 여러 품목을 등록한다(붙여넣기 그리드용). 한 행이 실패해도 나머지 행은 계속 처리된다 -
    // 행마다 독립적인 CreateAsync 호출이라 각자 자기 트랜잭션으로 커밋/실패한다.
    Task<IReadOnlyList<RegistrationBatchItemResultDto>> CreateManyAsync(IReadOnlyList<RegistrationCreateRequest> requests, CancellationToken cancellationToken = default);
    Task<RegistrationResultDto> UpdateAsync(int registrationId, RegistrationUpdateRequest request, CancellationToken cancellationToken = default);

    // OPER 화면 목록에서 LOT을 우클릭해 "LOT 정보 수정" 탭을 열 때 사용 - lotId로 이 Lot을 만든
    // 전산등록 레코드를 찾는다. 전산등록을 거치지 않고 생성된 예외적인 Lot이면 null.
    Task<RegistrationEditDto?> GetByLotIdAsync(int lotId, CancellationToken cancellationToken = default);
}
