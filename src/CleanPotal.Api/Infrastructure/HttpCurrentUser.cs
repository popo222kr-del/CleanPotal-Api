using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 현재 요청의 사용자. 토큰 검증 단계(Program.cs 의 OnTokenValidated)에서
/// <c>HttpContext.Items["auth_user"]</c> 에 넣어둔 User 를 그대로 읽으므로 추가 DB 조회가 없다.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public HttpCurrentUser(IHttpContextAccessor http) => _http = http;

    private User? User => _http.HttpContext?.Items["auth_user"] as User;

    public int? Id => User?.Id;
    public string RealName => User?.RealName ?? "";
    public bool IsAdmin => User?.IsAdmin ?? false;
}
