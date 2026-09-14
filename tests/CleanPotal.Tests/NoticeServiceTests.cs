using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 공지 = 작성자 책임 항목 → 수정·삭제 모두 작성자 본인 또는 관리자만.
/// 동시 수정 충돌과 변경 이력도 함께 확인한다.
/// </summary>
public class NoticeServiceTests
{
    private static readonly FakeCurrentUser 작성자 = FakeCurrentUser.Person(1, "박주언");
    private static readonly FakeCurrentUser 동료 = FakeCurrentUser.Person(2, "홍길동");

    private static async Task<(TestDb db, NoticeDto notice)> SeedNotice(FakeCurrentUser author)
    {
        var t = new TestDb();
        var svc = new NoticeService(t.Db, author);
        var dto = await svc.CreateAsync(new NoticeUpsertRequest("안전교육 안내", "본문"), author.RealName);
        return (t, dto);
    }

    [Fact]
    public async Task 등록하면_작성자_계정_ID_가_기록된다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        using var fresh = t.NewContext();
        var row = fresh.Notices.Single(x => x.Id == dto.Id);
        Assert.Equal(1, row.CreatorUserId);
        Assert.True(dto.CanModify);
    }

    [Fact]
    public async Task 남의_공지는_수정할_수_없다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var svc = new NoticeService(t.Db, 동료);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.UpdateAsync(dto.Id, new NoticeUpsertRequest("바꾼 제목", "바꾼 본문")));
    }

    [Fact]
    public async Task 남의_공지는_삭제할_수_없다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var svc = new NoticeService(t.Db, 동료);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(dto.Id));
    }

    [Fact]
    public async Task 관리자는_남의_공지도_수정_삭제할_수_있다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var admin = new NoticeService(t.Db, FakeCurrentUser.Admin());
        Assert.NotNull(await admin.UpdateAsync(dto.Id, new NoticeUpsertRequest("관리자 수정", "본문")));
        Assert.True(await admin.DeleteAsync(dto.Id));
    }

    [Fact]
    public async Task 작성자_본인은_수정할_수_있고_버전이_올라간다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var svc = new NoticeService(t.Db, 작성자);
        var updated = await svc.UpdateAsync(dto.Id, new NoticeUpsertRequest("고친 제목", "본문", dto.RowVersion));
        Assert.NotNull(updated);
        Assert.Equal("고친 제목", updated!.Title);
        Assert.Equal(dto.RowVersion + 1, updated.RowVersion);
    }

    [Fact]
    public async Task 그_사이_남이_먼저_저장했으면_충돌로_막힌다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var svc = new NoticeService(t.Db, 작성자);

        // 관리자가 먼저 저장 → 버전이 올라간다
        await new NoticeService(t.Db, FakeCurrentUser.Admin()).UpdateAsync(dto.Id, new NoticeUpsertRequest("먼저 수정", "본문"));

        // 작성자는 옛 버전을 들고 저장 시도 → 덮어쓰지 않고 충돌
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => svc.UpdateAsync(dto.Id, new NoticeUpsertRequest("나중 수정", "본문", dto.RowVersion)));

        using var fresh = t.NewContext();
        Assert.Equal("먼저 수정", fresh.Notices.Single(x => x.Id == dto.Id).Title);   // 먼저 저장한 내용이 남는다
    }

    [Fact]
    public async Task 버전을_안_보내면_충돌_검사를_건너뛴다()
    {
        // 구버전 클라이언트 호환 — 막으면 기존 화면이 전부 멈춘다.
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        await new NoticeService(t.Db, FakeCurrentUser.Admin()).UpdateAsync(dto.Id, new NoticeUpsertRequest("먼저", "본문"));
        var svc = new NoticeService(t.Db, 작성자);
        Assert.NotNull(await svc.UpdateAsync(dto.Id, new NoticeUpsertRequest("나중", "본문")));   // RowVersion 미지정
    }

    [Fact]
    public async Task 생성_수정_삭제가_변경_이력에_남는다()
    {
        var (t, dto) = await SeedNotice(작성자);
        using var _ = t;
        var svc = new NoticeService(t.Db, 작성자);
        await svc.UpdateAsync(dto.Id, new NoticeUpsertRequest("고친 제목", "본문"));
        await svc.DeleteAsync(dto.Id);

        using var fresh = t.NewContext();
        var logs = fresh.ContentAudits.Where(a => a.EntityType == "공지" && a.EntityId == dto.Id)
                        .OrderBy(a => a.Id).ToList();
        Assert.Equal(new[] { "생성", "수정", "삭제" }, logs.Select(l => l.Action));
        Assert.All(logs, l => Assert.Equal(1, l.ByUserId));
        Assert.All(logs, l => Assert.Equal("박주언", l.ByUserName));
        Assert.Contains("제목", logs[1].Detail);   // 무엇이 바뀌었는지 요약이 들어간다
    }

    [Fact]
    public async Task 없는_공지는_null_또는_false()
    {
        using var t = new TestDb();
        var svc = new NoticeService(t.Db, 작성자);
        Assert.Null(await svc.UpdateAsync(999, new NoticeUpsertRequest("x", "y")));
        Assert.False(await svc.DeleteAsync(999));
    }

    [Fact]
    public async Task 작성자_ID_없는_과거_공지는_이름으로_판정한다()
    {
        using var t = new TestDb();
        t.Db.Notices.Add(new Notice { Title = "예전 공지", Content = "본문", Author = "박주언", CreatorUserId = null });
        await t.Db.SaveChangesAsync();
        var id = t.Db.Notices.Single().Id;

        Assert.NotNull(await new NoticeService(t.Db, 작성자).UpdateAsync(id, new NoticeUpsertRequest("수정", "본문")));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => new NoticeService(t.Db, 동료).DeleteAsync(id));
    }
}
