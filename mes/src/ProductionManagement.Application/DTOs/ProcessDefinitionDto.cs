namespace ProductionManagement.Application.DTOs;

// 공정 마스터와 공정 플로우 화면이 쓰는 자료 묶음.
//   ProcessDefinitionDto / UpsertRequest  공정 하나(입고/세정/출고검사...)
//   ProcessRouteOptionDto                 플로우 선택 상자용 요약
//   ProcessRouteDetailDto + StepDetailDto 플로우 편집 화면용 상세(단계별 ProcessDefinitionId 포함)
public record ProcessDefinitionDto(int ProcessDefinitionId, string ProcessCode, string ProcessName, int OperCode, bool IsActive);

public record ProcessDefinitionUpsertRequest(string ProcessCode, string ProcessName);

public record ProcessRouteOptionDto(int ProcessRouteId, string RouteCode, string RouteName);

// "공정 관리" 탭의 플로우 관리 섹션 전용(2026-08-24 신설) - 각 단계의 ProcessDefinitionId를 그대로
// 노출해서 편집 화면이 "이미 포함된 공정"을 식별할 수 있게 한다.
public record ProcessRouteDetailDto(int ProcessRouteId, string RouteCode, string RouteName, bool IsActive, IReadOnlyList<ProcessRouteStepDetailDto> Steps);

public record ProcessRouteStepDetailDto(int StepOrder, int ProcessDefinitionId, string ProcessCode, string ProcessName);

public record ProcessRouteUpsertRequest(string RouteCode, string RouteName, IReadOnlyList<int> ProcessDefinitionIds);
