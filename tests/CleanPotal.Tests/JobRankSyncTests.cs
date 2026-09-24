using System.Text.RegularExpressions;
using CleanPotal.Core;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>직급 목록은 서버(JobRank.All)와 사용자 관리 화면(Users.tsx 의 RANKS) 두 곳에 있다 — 어긋나면 잡는다.</summary>
public class JobRankSyncTests
{
    [Fact]
    public void 화면의_직급_목록이_서버와_같다()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CleanPotal.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var tsx = File.ReadAllText(Path.Combine(dir!.FullName, "client", "src", "pages", "Users.tsx"));
        var m = Regex.Match(tsx, @"const RANKS = \[(?<list>[^\]]*)\]");
        Assert.True(m.Success, "Users.tsx 에서 RANKS 를 찾지 못했습니다.");
        var ranks = Regex.Matches(m.Groups["list"].Value, "'([^']*)'").Select(x => x.Groups[1].Value).ToArray();

        Assert.Equal(JobRank.All, ranks);
    }
}
