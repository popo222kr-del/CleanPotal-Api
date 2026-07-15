using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IAuthService
{
    /// <summary>아이디/비밀번호 검증 후 JWT 발급. 실패 시 null.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>본인 아이디/비밀번호 변경. 성공 시 갱신된 토큰 반환.</summary>
    Task<(bool ok, string? error, LoginResponse? res)> ChangeCredentialsAsync(int userId, ChangeCredentialsRequest request);
}
