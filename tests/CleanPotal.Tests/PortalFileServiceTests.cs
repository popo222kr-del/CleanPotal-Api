using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleanPotal.Tests;

public class PortalFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"portal-files-{Guid.NewGuid():N}");

    [Fact]
    public async Task 허용된_루트의_등록_파일만_해석한다()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "공유 문서.xlsx");
        await File.WriteAllTextAsync(path, "test");

        using var db = new TestDb();
        var group = new PortalGroup { Name = "시험" };
        var item = new PortalItem { Group = group, Title = "문서", Path = path, Type = "excel" };
        db.Db.Add(item);
        await db.Db.SaveChangesAsync();

        var service = new PortalFileService(db.Db, Configuration(_root));
        var result = await service.ResolveAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(path), result!.FullPath);
        Assert.Equal("공유 문서.xlsx", result.FileName);
    }

    [Fact]
    public async Task 허용된_루트_밖의_파일은_거부한다()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(outside, "test");
        try
        {
            using var db = new TestDb();
            var group = new PortalGroup { Name = "시험" };
            var item = new PortalItem { Group = group, Title = "외부", Path = outside, Type = "excel" };
            db.Db.Add(item);
            await db.Db.SaveChangesAsync();

            var service = new PortalFileService(db.Db, Configuration(_root));
            Assert.Null(await service.ResolveAsync(item.Id));
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    private static IConfiguration Configuration(string root)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PortalFiles:AllowedRoots:0"] = root
            })
            .Build();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
