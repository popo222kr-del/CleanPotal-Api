using CleanPotal.Core;
using CleanPotal.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>첨부 칸에 새 base64 사진을 넣지 못하게 — 옛 기록에 이미 있던 것은 그대로 둔다(다른 칸만 고쳐도 저장되게).</summary>
public class InlineDataGuardTests
{
    private const string Img = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD";

    [Fact]
    public void 첨부_참조만_있으면_통과() =>
        InlineDataGuard.EnsureNoNewInline("""["att:12|사진.jpg|image/jpeg"]""", null, "사진");

    [Fact]
    public void 새_base64_는_거절() =>
        Assert.Throws<BusinessRuleException>(() => InlineDataGuard.EnsureNoNewInline($"[\"{Img}\"]", "[]", "사진"));

    [Fact]
    public void 이미_있던_base64_는_그대로_저장된다() =>
        InlineDataGuard.EnsureNoNewInline($"[\"{Img}\",\"att:3|a.jpg|image/jpeg\"]", $"[\"{Img}\"]", "사진");

    [Fact]
    public void 빈_값은_통과()
    {
        InlineDataGuard.EnsureNoNewInline(null, null, "사진");
        InlineDataGuard.EnsureNoNewInline("", "x", "사진");
    }
}
