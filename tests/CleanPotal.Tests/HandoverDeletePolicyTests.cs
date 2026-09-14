using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 인수인계 = 공동 업무 → <b>수정은 등급 2 면 누구나</b>, <b>삭제만 작성자 본인·관리자</b>.
/// (삭제·취소 정책은 수정과 별도로 적용한다는 결정)
/// </summary>
public class HandoverDeletePolicyTests
{
    private static readonly FakeCurrentUser 등록자 = FakeCurrentUser.Person(1, "박주언");
    private static readonly FakeCurrentUser 교대근무자 = FakeCurrentUser.Person(2, "홍길동");

    private static HandoverUpsertRequest Req(string vendor = "영신쿼츠", string content = "세정 의뢰", string? memo = null, int? rowVersion = null)
        => new(vendor, "담당자", content, null, null, "미정", memo ?? "", false, "진행", null, rowVersion);

    private static async Task<(TestDb t, HandoverDto dto)> Seed()
    {
        var t = new TestDb();
        var dto = await new HandoverService(t.Db, 등록자).CreateAsync(Req(), 등록자.RealName);
        return (t, dto);
    }

    [Fact]
    public async Task 등록하면_작성자_계정_ID_가_기록된다()
    {
        var (t, dto) = await Seed();
        using var _ = t;
        using var fresh = t.NewContext();
        Assert.Equal(1, fresh.Handovers.Single(x => x.Id == dto.Id).CreatorUserId);
        Assert.True(dto.CanDelete);
    }

    [Fact]
    public async Task 남이_등록한_항목도_수정할_수_있다()
    {
        // 교대 근무자가 이어서 채우는 것이 정상 업무다.
        var (t, dto) = await Seed();
        using var _ = t;
        var updated = await new HandoverService(t.Db, 교대근무자)
            .UpdateAsync(dto.Id, Req(memo: "야간에 확인함"), 교대근무자.RealName, isAdmin: false);
        Assert.NotNull(updated);
        Assert.False(updated!.CanDelete);   // 수정은 되지만 삭제 버튼은 안 보인다
    }

    [Fact]
    public async Task 남이_등록한_항목은_삭제할_수_없다()
    {
        var (t, dto) = await Seed();
        using var _ = t;
        await Assert.ThrowsAsync<ForbiddenException>(
            () => new HandoverService(t.Db, 교대근무자).DeleteAsync(dto.Id, isAdmin: false));
    }

    [Fact]
    public async Task 등록자_본인과_관리자는_삭제할_수_있다()
    {
        var (t1, d1) = await Seed();
        using (t1) Assert.True(await new HandoverService(t1.Db, 등록자).DeleteAsync(d1.Id, isAdmin: false));

        var (t2, d2) = await Seed();
        using (t2) Assert.True(await new HandoverService(t2.Db, FakeCurrentUser.Admin()).DeleteAsync(d2.Id, isAdmin: true));
    }

    [Fact]
    public async Task 동시_수정은_충돌로_막힌다()
    {
        var (t, dto) = await Seed();
        using var _ = t;
        await new HandoverService(t.Db, 교대근무자).UpdateAsync(dto.Id, Req(memo: "먼저 저장"), 교대근무자.RealName, false);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new HandoverService(t.Db, 등록자).UpdateAsync(dto.Id, Req(memo: "나중 저장", rowVersion: dto.RowVersion), 등록자.RealName, false));
    }

    [Fact]
    public async Task 상태변경도_이력에_남는다()
    {
        var (t, dto) = await Seed();
        using var _ = t;
        await new HandoverService(t.Db, 교대근무자).ChangeStatusAsync(dto.Id, "포장", 교대근무자.RealName, false);

        using var fresh = t.NewContext();
        var log = fresh.ContentAudits.Single(a => a.EntityType == "인수인계" && a.Action == "상태변경");
        Assert.Equal("진행 → 포장", log.Detail);
        Assert.Equal(2, log.ByUserId);
    }
}
