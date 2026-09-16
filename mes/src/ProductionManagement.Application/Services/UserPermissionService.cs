using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 관리자 화면 "사용자 관리" 탭의 백엔드. 계정을 새로 만드는 기능은 없다 - Windows 계정이 앱을
// 처음 실행하면 IAuthorizationService가 자동으로 Worker로 등록해 두므로, 여기서는 이미 등록된
// 계정의 Role/개별 권한만 바꾼다.
public class UserPermissionService : IUserPermissionService
{
    private readonly IRepository<User, int> _users;
    private readonly IRepository<UserPermission, int> _userPermissions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;
    private readonly IPasswordHasher _passwordHasher;

    public UserPermissionService(
        IRepository<User, int> users,
        IRepository<UserPermission, int> userPermissions,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization,
        IPasswordHasher passwordHasher)
    {
        _users = users;
        _userPermissions = userPermissions;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _users.ListAllAsync(cancellationToken);
        return users
            .OrderBy(u => u.LoginId)
            .Select(u => new UserDto(u.Id, u.LoginId, u.DisplayName, u.TeamName, u.Role, u.IsActive, u.UpdatedAt))
            .ToList();
    }

    public async Task<UserDetailDto> GetDetailAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        var granted = await _userPermissions.ListAsync(p => p.UserId == userId, cancellationToken);

        return new UserDetailDto(user.Id, user.LoginId, user.DisplayName, user.TeamName, user.Role, user.IsActive, granted.Select(p => p.PermissionCode).ToList());
    }

    public async Task UpdateRoleAsync(UpdateUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        user.Role = request.Role;
        user.UpdatedAt = DateTime.Now;
        _users.Update(user);

        var actor = _currentUser.GetCurrentUser();
        _auditLogger.Log("User.RoleChange", nameof(User), user.LoginId, actor, $"NewRole={request.Role}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetPermissionAsync(SetUserPermissionRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        var actor = _currentUser.GetCurrentUser();
        var existing = (await _userPermissions.ListAsync(
            p => p.UserId == request.UserId && p.PermissionCode == request.PermissionCode, cancellationToken)).FirstOrDefault();

        if (request.Granted)
        {
            if (existing is null)
            {
                await _userPermissions.AddAsync(new UserPermission
                {
                    UserId = request.UserId,
                    PermissionCode = request.PermissionCode,
                    GrantedBy = actor,
                    GrantedAt = DateTime.Now
                }, cancellationToken);
                _auditLogger.Log("User.PermissionGrant", nameof(User), user.LoginId, actor, $"Permission={request.PermissionCode}");
            }
        }
        else if (existing is not null)
        {
            _userPermissions.Remove(existing);
            _auditLogger.Log("User.PermissionRevoke", nameof(User), user.LoginId, actor, $"Permission={request.PermissionCode}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CreateAccountAsync(CreateUserAccountRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);

        var loginId = (request.LoginId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loginId))
        {
            throw new ValidationException(new[] { "아이디를 입력하세요." });
        }
        if (string.IsNullOrWhiteSpace(request.InitialPassword))
        {
            throw new ValidationException(new[] { "초기 비밀번호를 입력하세요." });
        }
        if (await _users.ExistsAsync(u => u.LoginId == loginId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 존재하는 아이디입니다: {loginId}" });
        }

        var now = DateTime.Now;
        var user = new User
        {
            LoginId = loginId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            TeamName = string.IsNullOrWhiteSpace(request.TeamName) ? null : request.TeamName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.InitialPassword),
            Role = request.Role,
            IsActive = true,
            MustChangePassword = true, // 최초 로그인 시 비밀번호 변경 유도
            CreatedAt = now,
            UpdatedAt = now
        };
        await _users.AddAsync(user, cancellationToken);
        _auditLogger.Log("User.Create", nameof(User), loginId, _currentUser.GetCurrentUser(), $"Role={request.Role}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    // 2026-08-31 피드백(#5): 본인(또는 관리자) 계정 정보(아이디/이름/팀명) 수정.
    public async Task UpdateProfileAsync(UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        // 본인 계정이면 별도 권한 없이 수정 가능. 남의 계정을 수정하려면 사용자 관리 권한이 필요하다.
        var isSelf = string.Equals(user.LoginId, _currentUser.GetCurrentUser(), StringComparison.OrdinalIgnoreCase);
        if (!isSelf)
        {
            await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);
        }

        var loginId = (request.LoginId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loginId))
        {
            throw new ValidationException(new[] { "아이디를 입력하세요." });
        }
        if (await _users.ExistsAsync(u => u.LoginId == loginId && u.Id != request.UserId, cancellationToken))
        {
            throw new ValidationException(new[] { $"이미 존재하는 아이디입니다: {loginId}" });
        }

        user.LoginId = loginId;
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        user.TeamName = string.IsNullOrWhiteSpace(request.TeamName) ? null : request.TeamName.Trim();
        user.UpdatedAt = DateTime.Now;
        _users.Update(user);
        _auditLogger.Log("User.UpdateProfile", nameof(User), loginId, _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetUserPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");

        // 2026-08-31 피드백(#5): 본인 비밀번호는 권한 없이 변경 가능(변경 후 재변경 강요 안 함). 관리자가
        // 남의 비밀번호를 초기화하는 경우에만 사용자 관리 권한이 필요하고, 최초 로그인 시 재변경을 유도한다.
        var isSelf = string.Equals(user.LoginId, _currentUser.GetCurrentUser(), StringComparison.OrdinalIgnoreCase);
        if (!isSelf)
        {
            await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new ValidationException(new[] { "새 비밀번호를 입력하세요." });
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = !isSelf;
        user.UpdatedAt = DateTime.Now;
        _users.Update(user);
        _auditLogger.Log("User.ResetPassword", nameof(User), user.LoginId, _currentUser.GetCurrentUser());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(SetUserActiveRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminUserManagement, cancellationToken);

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("사용자를 찾을 수 없습니다.");
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.Now;
        _users.Update(user);
        _auditLogger.Log("User.SetActive", nameof(User), user.LoginId, _currentUser.GetCurrentUser(), $"IsActive={request.IsActive}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
