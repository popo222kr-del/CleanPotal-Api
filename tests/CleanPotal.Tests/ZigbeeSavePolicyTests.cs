using CleanPotal.Core.Iot;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 이력 저장 정책. 값이 그대로인 메시지까지 전부 쌓으면 표가 감당이 안 되고,
/// 값이 바뀔 때만 쌓으면 센서가 언제까지 살아 있었는지가 사라진다. 그 사이를 지킨다.
/// </summary>
public class ZigbeeSavePolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 14, 30, 0);

    [Fact]
    public void 첫_값은_무조건_남긴다()
        => Assert.True(ZigbeeSavePolicy.ShouldSave(null, 28.5, 46.7, Now, 60));

    [Fact]
    public void 값이_바뀌면_간격과_상관없이_남긴다()
    {
        var last = new ZigbeeSavePolicy.Saved(28.5, 46.7, Now.AddSeconds(-1));
        Assert.True(ZigbeeSavePolicy.ShouldSave(last, 28.6, 46.7, Now, 60));   // 온도가 바뀜
        Assert.True(ZigbeeSavePolicy.ShouldSave(last, 28.5, 47.0, Now, 60));   // 습도가 바뀜
    }

    [Fact]
    public void 값이_같고_간격_전이면_남기지_않는다()
    {
        var last = new ZigbeeSavePolicy.Saved(28.5, 46.7, Now.AddSeconds(-30));
        Assert.False(ZigbeeSavePolicy.ShouldSave(last, 28.5, 46.7, Now, 60));
    }

    [Fact]
    public void 값이_같아도_간격이_지나면_남긴다()
    {
        var last = new ZigbeeSavePolicy.Saved(28.5, 46.7, Now.AddSeconds(-60));
        Assert.True(ZigbeeSavePolicy.ShouldSave(last, 28.5, 46.7, Now, 60));
    }

    [Fact]
    public void 값이_비어도_바뀐_것으로_본다()
    {
        var last = new ZigbeeSavePolicy.Saved(28.5, 46.7, Now.AddSeconds(-1));
        Assert.True(ZigbeeSavePolicy.ShouldSave(last, null, 46.7, Now, 60));
    }

    [Fact]
    public void 간격을_0으로_두면_올_때마다_남긴다()
    {
        var last = new ZigbeeSavePolicy.Saved(28.5, 46.7, Now);
        Assert.True(ZigbeeSavePolicy.ShouldSave(last, 28.5, 46.7, Now, 0));
    }
}
