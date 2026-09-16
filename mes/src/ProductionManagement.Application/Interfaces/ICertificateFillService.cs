namespace ProductionManagement.Application.Interfaces;

// LOT 하나의 최신 성적서 파일에 앱 데이터(Part Information + 입·출고 검사값)를 반영한다.
//  - 전산등록 직후: Part Information이 채워진다(검사값은 아직 없음).
//  - 입고/출고 검사 저장 후: 그 시점까지의 검사값이 함께 반영된다(공정 이동 시 자동 반영).
// 성적서 반영 실패가 등록/검사 저장 자체를 막으면 안 되므로 성공 여부(bool)만 돌려주고 예외는 삼킨다.
public interface ICertificateFillService
{
    Task<bool> FillAsync(int lotId, CancellationToken cancellationToken = default);

    // 특이사항 이미지를 이 LOT의 최신 성적서에 삽입한다(2026-08-27 피드백: 특이사항은 성적서 Excel에 삽입).
    Task<bool> InsertAbnormalImageAsync(int lotId, string imagePath, CancellationToken cancellationToken = default);
}
