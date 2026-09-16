namespace ProductionManagement.Application.Interfaces;

// Phase 13(권한)에서 Windows 계정 기반 Role 조회로 확장된다. 지금은 계정명만 제공한다.
public interface ICurrentUserProvider
{
    string GetCurrentUser();
}
