using CleanPotal.Core.DTOs;

namespace CleanPotal.Core.Interfaces;

public interface IAuthService
{
    /// <summary>아이디/비밀번호 검증 후 JWT 발급. 실패 시 null.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}
