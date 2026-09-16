namespace ProductionManagement.Application.Interfaces;

// 비밀번호 표준 솔트 해시(PBKDF2). 저장 형식은 구현이 정한다("iterations.salt.hash" 등).
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
