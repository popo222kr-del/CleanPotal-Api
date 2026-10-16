using CleanPotal.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>기존 MES 파일 서버 — 제품 사진·성적서 양식·LOT 성적서 찾기(폴더는 임시 폴더로 흉내)</summary>
public sealed class LegacyMesFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mesfiles-" + Guid.NewGuid().ToString("N"));

    public LegacyMesFilesTests()
    {
        foreach (var d in new[] { "MatImage", "Inspection_PDF", "Inspection", "Inspection_cov", "Inspection/1SB240503011P" })
            Directory.CreateDirectory(Path.Combine(_root, d));
        File.WriteAllText(Path.Combine(_root, "MatImage", "600046777.jpg"), "img");
        File.WriteAllText(Path.Combine(_root, "Inspection_PDF", "1SA240503035P.pdf"), "pdf");
        File.WriteAllText(Path.Combine(_root, "Inspection_PDF", "1SA240503035P.xlsx"), "xlsx");
        File.WriteAllText(Path.Combine(_root, "Inspection", "1SA240503035P.xlsx"), "dup");
        File.WriteAllText(Path.Combine(_root, "Inspection", "1SB240503011P", "1SB240503011P_v2.xlsx"), "sub");
        File.WriteAllText(Path.Combine(_root, "Inspection", "CAP COVER QTZ.xlsx"), "tpl");
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "x");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private LegacyMesFiles Files() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LegacyMes:FileRoot"] = _root }).Build());

    [Fact]
    public void 제품_사진은_제품코드_jpg()
    {
        Assert.EndsWith("600046777.jpg", Files().MatImage("600046777"));
        Assert.Null(Files().MatImage("600000000"));
        Assert.Null(Files().MatImage("../secret"));
    }

    [Fact]
    public void LOT_성적서는_PDF_폴더가_먼저_같은_이름은_한_번만()
    {
        var c = Files().LotCerts("1SA240503035P");
        Assert.Equal(["1SA240503035P.pdf", "1SA240503035P.xlsx"], c.Select(x => x.Name));
        Assert.All(c, x => Assert.Equal("Inspection_PDF", x.Folder));
        Assert.Equal(["1SB240503011P_v2.xlsx"], Files().LotCerts("1SB240503011P").Select(x => x.Name));
        Assert.Empty(Files().LotCerts("..\\x"));
    }

    [Fact]
    public void 성적서_양식은_파일명만_경로가_섞이면_찾지_않는다()
    {
        Assert.EndsWith("CAP COVER QTZ.xlsx", Files().Template("CAP COVER QTZ.xlsx"));
        Assert.Null(Files().Template("없는 양식.xlsx"));
        Assert.Null(Files().Template("../secret.txt"));
        Assert.Null(Files().Template("..\\secret.txt"));
        Assert.Null(Files().Template(""));
    }
}
