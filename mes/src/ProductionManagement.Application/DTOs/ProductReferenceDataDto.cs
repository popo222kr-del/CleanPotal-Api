using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// 제품 셋업 화면에서 고르는 선택지들(기준정보).
//   LineOptionDto      LINE 목록
//   RecipeOptionDto    레시피 목록(공정·판독시간 포함)
//   ParameterOptionDto 검사 파라미터 목록
// 전부 "무엇을 고를 수 있는가"를 내려주는 용도이고, 실제 배정 결과는 별도 엔티티에 저장된다.
public record LineOptionDto(int LineId, string Code, string Description);

public record RecipeOptionDto(int RecipeId, string Code, string Description, int OperCode, int? ReadTimeMinutes);

// 2026-08-25: 제품 셋업 "파라미터(검사 항목)" 표를 참조 이미지처럼 다루기 위해 OPER/VALUE COUNT/
// MIN/MAX/UNIT/USE(IsActive)까지 담는다.
public record ParameterOptionDto(
    int ParameterId, string Code, string Description, ParameterType ParameterType,
    string? Oper, int ValueCount, decimal? MinValue, decimal? MaxValue, string? Unit, bool IsActive,
    int? ProductId = null,
    // 성적서 표기명(마스터). 성적서 항목명과 매칭할 때 Code 대신 이 값을 우선한다.
    string? CertificateLabel = null);

// 파라미터 신규저장/수정용(2026-08-25 - 하단 편집 폼에서 목록에 추가하는 방식). 2026-08-26부터 제품별이라
// ProductId가 필수(어느 제품의 파라미터인지).
public record ParameterUpsertRequest(
    string Code, string Description, ParameterType ParameterType,
    string? Oper, int ValueCount, decimal? MinValue, decimal? MaxValue, string? Unit, bool IsActive,
    int? ProductId = null,
    string? CertificateLabel = null);

// RecipeDefinitionId/RecipeCode가 null이면 이 제품은 이 공정에 아직 레시피를 지정하지 않은 상태.
public record ProductRecipeSlotDto(int ProcessDefinitionId, string ProcessName, int OperCode, int? RecipeDefinitionId, string? RecipeCode);

// 2026-08-26: 제품별 "공정별 레시피" 다중 설정용. 한 OPER에 여러 레시피를 두고 그중 하나를 MAIN으로.
// USE FLAG(IsActive), MIN/MAX, MAIN RCP(IsMain), READ TIME(레시피 정의값)을 함께 보여준다.
public record ProductRecipeDto(
    int AssignmentId, int RecipeDefinitionId, string RecipeCode, string RecipeDescription,
    int ProcessDefinitionId, int OperCode, decimal? MinValue, decimal? MaxValue,
    bool IsMain, bool IsActive, int? ReadTimeMinutes);

// 제품별 레시피 신규저장/수정용(GENERAL 폼). ProcessDefinitionId는 선택한 레시피의 OperCode로 서비스가 채운다.
public record ProductRecipeUpsertRequest(
    int ProductId, int RecipeDefinitionId, decimal? MinValue, decimal? MaxValue, bool IsMain, bool IsActive);

// 2026-08-26: 제품 셋업 파라미터 편집 폼의 PARAMETER/OPER를 자유 입력이 아니라 "선택"으로 바꾸기 위한 옵션.
// 파라미터 카탈로그(코드+설명) - ProductId가 없는 참조 행에서 코드별 1건씩. DESC는 선택 시 자동 채움.
public record ParameterCatalogDto(string Code, string Description)
{
    public string Display => string.IsNullOrWhiteSpace(Description) ? Code : $"{Code}  ({Description})";
}

// OPER 옵션(공정 정의) - 코드 + 공정명. 파라미터의 Oper에는 OperCode 문자열이 저장된다.
public record OperOptionDto(int OperCode, string ProcessName)
{
    public string Display => $"{OperCode}  {ProcessName}";
}
