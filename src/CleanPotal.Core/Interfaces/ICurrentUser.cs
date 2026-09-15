namespace CleanPotal.Core.Interfaces;

/// <summary>
/// 지금 요청을 보낸 사용자. 작성자 본인 여부를 판단할 때 쓴다.
/// 구현은 Api 계층(HttpCurrentUser)이 하고, 서비스는 이 인터페이스만 의존한다.
/// </summary>
public interface ICurrentUser
{
    /// <summary>사용자 PK. 로그인하지 않았으면 null.</summary>
    int? Id { get; }

    /// <summary>실명. 과거 데이터(작성자 ID 가 비어 있는 행) 대조용.</summary>
    string RealName { get; }

    bool IsAdmin { get; }

    /// <summary>소속 부서. 자기 소속 인원만 보여줄 때 기준이 된다.</summary>
    string Department { get; }

    /// <summary>소속 팀. 부서가 비어 있을 때의 대체 기준.</summary>
    string TeamName { get; }
}
