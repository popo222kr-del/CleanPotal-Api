using CleanPotal.Core;
using CleanPotal.Core.Security;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 자료 단위 권한 — "작성자 본인 또는 관리자만" 규칙.
/// 이름이 아니라 계정 ID 로 판정하는지가 핵심이다.
/// </summary>
public class ContentOwnershipTests
{
    private static readonly FakeCurrentUser 박주언 = FakeCurrentUser.Person(1, "박주언");

    [Fact]
    public void 작성자_본인이면_통과()
        => Assert.True(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: 1, creatorName: "박주언"));

    [Fact]
    public void 다른_사람이면_막힌다()
        => Assert.False(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: 2, creatorName: "홍길동"));

    [Fact]
    public void 관리자는_남의_글도_통과()
        => Assert.True(ContentOwnership.IsOwnerOrAdmin(FakeCurrentUser.Admin(), creatorUserId: 2, creatorName: "홍길동"));

    [Fact]
    public void 동명이인은_계정_ID_로_구분된다()
    {
        // 이름이 같아도 계정이 다르면 남의 글이다 — 이름 비교였다면 통과했을 경우.
        Assert.False(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: 2, creatorName: "박주언"));
    }

    [Fact]
    public void 개명해도_계정_ID_가_같으면_본인이다()
    {
        var 개명후 = FakeCurrentUser.Person(1, "박주언A");
        Assert.True(ContentOwnership.IsOwnerOrAdmin(개명후, creatorUserId: 1, creatorName: "박주언"));
    }

    [Fact]
    public void 작성자_ID_가_없는_과거_행은_이름으로_대조한다()
    {
        Assert.True(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: null, creatorName: "박주언"));
        Assert.True(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: null, creatorName: " 박주언 "));   // 공백 무시
        Assert.False(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: null, creatorName: "홍길동"));
    }

    [Fact]
    public void 작성자_정보가_아예_없는_과거_행은_작성자_미상으로_통과한다()
    {
        // 회의록·주간보고처럼 작성자를 기록한 적이 없는 자료까지 막으면
        // 관리자 외에는 아무도 정리할 수 없게 된다.
        Assert.True(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: null, creatorName: ""));
        Assert.True(ContentOwnership.IsOwnerOrAdmin(박주언, creatorUserId: null, creatorName: null));
    }

    [Fact]
    public void 로그인하지_않았으면_막힌다()
        => Assert.False(ContentOwnership.IsOwnerOrAdmin(FakeCurrentUser.Anonymous(), creatorUserId: 1, creatorName: "박주언"));

    [Fact]
    public void EnsureOwnerOrAdmin_는_권한이_없으면_ForbiddenException()
    {
        var ex = Assert.Throws<ForbiddenException>(
            () => ContentOwnership.EnsureOwnerOrAdmin(박주언, 2, "홍길동", "공지", "삭제"));
        Assert.Contains("공지", ex.Message);
        Assert.Contains("삭제", ex.Message);
    }
}
