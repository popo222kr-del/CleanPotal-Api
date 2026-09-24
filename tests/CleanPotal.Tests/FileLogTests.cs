using CleanPotal.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>전역 Console 을 바꾸는 테스트는 다른 테스트와 동시에 돌리지 않는다.</summary>
[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection { }

/// <summary>IIS 에서 사라지던 콘솔 출력을 날짜별 파일로 남기고, 오래된 파일은 지운다.</summary>
[Collection("Console")]
public class FileLogTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cp-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void 보관_기간이_지난_파일만_지운다()
    {
        var dir = TempDir();
        try
        {
            foreach (var name in new[] { "portal-20260801.log", "portal-20260825.log", "portal-20260924.log", "other.log", "portal-bad.log" })
                File.WriteAllText(Path.Combine(dir, name), "x");

            var removed = FileLog.Purge(dir, 30, new DateTime(2026, 9, 24));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(Path.Combine(dir, "portal-20260801.log")));
            Assert.True(File.Exists(Path.Combine(dir, "portal-20260825.log")));
            Assert.True(File.Exists(Path.Combine(dir, "other.log")));
            Assert.True(File.Exists(Path.Combine(dir, "portal-bad.log")));
            Assert.Equal(0, FileLog.Purge(dir, 0, new DateTime(2030, 1, 1)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void 콘솔과_파일에_같이_쓰고_줄마다_시각을_붙인다()
    {
        var dir = TempDir();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:File:Enabled"] = "true",
            ["Logging:File:Path"] = dir,
        }).Build();
        var original = Console.Out;
        try
        {
            var console = new StringWriter();
            Console.SetOut(console);
            Assert.Equal(dir, FileLog.Start(cfg, dir, isDevelopment: true));

            Console.WriteLine("[schema] 첫 줄");
            Console.Write("둘째 ");
            Console.WriteLine("줄");

            // 윈도우에서는 쓰는 중인 파일을 File.ReadAllLines 로 열 수 없다 — 먼저 닫고 읽는다.
            FileLog.Stop();
            Assert.Contains("[schema] 첫 줄", console.ToString());
            var lines = File.ReadAllLines(Directory.GetFiles(dir, "portal-*.log").Single());
            Assert.Equal(2, lines.Length);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[schema\] 첫 줄$", lines[0]);
            Assert.EndsWith(" 둘째 줄", lines[1]);
        }
        finally
        {
            FileLog.Stop();
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void 개발환경은_기본으로_끈다()
    {
        var cfg = new ConfigurationBuilder().Build();
        Assert.Null(FileLog.Start(cfg, Path.GetTempPath(), isDevelopment: true));
    }
}
