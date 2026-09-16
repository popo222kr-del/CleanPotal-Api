using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Infrastructure.Security;

// 로그인 인증. 아이디로 계정을 찾아 비밀번호 해시를 대조하고, 중지된 계정은 거부한다.
// 계정이 하나도 없는 새 DB에서는 최초 관리자(1214/1214)를 한 번 만들어 준다 - 그 계정으로 들어가
// 실제 사용자를 등록하고 비밀번호를 바꾸는 것이 첫 설치 절차다.
public class AuthService : IAuthService
{
    // 최초 관리자(마스터) 기본 계정 - 로그인 계정이 하나도 없을 때만 시드한다.
    private const string DefaultAdminLoginId = "1214";
    private const string DefaultAdminPassword = "1214";

    private readonly IRepository<User, int> _users;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;

    public AuthService(IRepository<User, int> users, IPasswordHasher hasher, IUnitOfWork unitOfWork)
    {
        _users = users;
        _hasher = hasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<AuthUser?> LoginAsync(string loginId, string password, CancellationToken cancellationToken = default)
    {
        var id = (loginId ?? string.Empty).Trim();
        var user = (await _users.ListAsync(u => u.LoginId == id, cancellationToken)).FirstOrDefault();
        if (user is null || !user.IsActive)
        {
            return null;
        }
        if (!_hasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            return null;
        }
        return new AuthUser(user.Id, user.LoginId, user.DisplayName, user.Role, user.MustChangePassword);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return false;
        }
        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.Now;
        _users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken cancellationToken = default)
    {
        var hasAccount = await _users.ExistsAsync(u => u.LoginId != null && u.LoginId != "", cancellationToken);
        if (hasAccount)
        {
            return;
        }

        var now = DateTime.Now;
        await _users.AddAsync(new User
        {
            LoginId = DefaultAdminLoginId,
            PasswordHash = _hasher.Hash(DefaultAdminPassword),
            DisplayName = "마스터",
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
