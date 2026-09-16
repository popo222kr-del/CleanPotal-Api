namespace ProductionManagement.Application.Interfaces;

// LOT 번호 채번기. 번호는 사용자가 입력하지 못하고 반드시 여기를 거쳐 나온다
// - 중복이나 빈 번호가 생기지 않게 하기 위해서다.
public interface ILotNumberGenerator
{
    // code: 전산등록 시 사용자가 입력하는 2자리 코드(예: SS/SA/SU - 특정 의미를 갖지 않는 임의값,
    // 2026-08-21 피드백). 최종 LOT 번호 형식은 "1{code}{yyMMdd}{일별순번:3자리}P" - 자세한 설계는
    // SqliteLotNumberGenerator.cs 참고.
    Task<string> NextAsync(string code, CancellationToken cancellationToken = default);
}
