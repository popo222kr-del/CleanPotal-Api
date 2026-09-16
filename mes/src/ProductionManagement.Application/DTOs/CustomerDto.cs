namespace ProductionManagement.Application.DTOs;

// 업체 관리 화면이 주고받는 자료 묶음.
//   CustomerDto           목록/선택 상자에 뿌리는 업체 한 건
//   CustomerUpsertRequest 등록·수정 시 화면이 보내는 입력값
public record CustomerDto(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    string ExportPrefix,
    int? LineDefinitionId,
    string? LineCode,
    string? LineDescription,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CustomerUpsertRequest(string CustomerCode, string CustomerName, string ExportPrefix, int? LineDefinitionId);
