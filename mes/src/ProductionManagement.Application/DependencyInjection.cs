using Microsoft.Extensions.DependencyInjection;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Services;

namespace ProductionManagement.Application;

// Application 계층의 서비스 등록. 앱은 AddApplication 한 줄만 부르면 된다.
// 여기 등록되는 것은 업무 규칙을 담은 서비스뿐이고, DB·파일·인증 같은 바깥 세계 구현은
// Infrastructure의 AddInfrastructure가 맡는다 - 계층 경계를 이 두 파일이 지킨다.
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ILotService, LotService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IProcessTransitionService, ProcessTransitionService>();
        services.AddScoped<IProcessDefinitionService, ProcessDefinitionService>();
        services.AddScoped<IOperActionService, OperActionService>();
        services.AddScoped<ITranDefinitionService, TranDefinitionService>();
        services.AddScoped<IOperQueryService, OperQueryService>();
        services.AddScoped<IHoldService, HoldService>();
        services.AddScoped<IReworkService, ReworkService>();
        services.AddScoped<IProcessHistoryVoidService, ProcessHistoryVoidService>();
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IUserPermissionService, UserPermissionService>();
        services.AddScoped<IProductFlowService, ProductFlowService>();
        services.AddScoped<IProductReferenceDataService, ProductReferenceDataService>();
        services.AddScoped<IProductPriceService, ProductPriceService>();
        services.AddScoped<IInspectionService, InspectionService>();
        services.AddScoped<ILotHistoryService, LotHistoryService>();
        return services;
    }
}
