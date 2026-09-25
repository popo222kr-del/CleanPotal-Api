using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>화면 배지(개발·테스트·운영)와 빌드 정보 — 세 서버가 같은 화면이라 어디에 접속했는지 구분하는 근거.</summary>
public sealed class PortalAboutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("about-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { /* 임시 폴더 */ } }

    [Theory]
    [InlineData(null, true, "dev", "개발")]
    [InlineData(null, false, "prod", "운영")]
    [InlineData("test", false, "test", "테스트")]
    [InlineData(" TEST ", false, "test", "테스트")]
    [InlineData("prod", true, "prod", "운영")]
    public void 설정과_개발_모드로_서버_종류를_정한다(string? name, bool isDev, string env, string label)
    {
        var a = PortalAbout.Load(name, isDev, _dir);
        Assert.Equal(env, a.Env);
        Assert.Equal(label, a.EnvLabel);
        Assert.Equal("", a.Commit);   // build-info.json 이 없으면 비운다
    }

    [Fact]
    public void build_info_json_에서_커밋과_빌드_시각을_읽는다()
    {
        File.WriteAllText(Path.Combine(_dir, "build-info.json"),
            """{"commit":"b06f185","subject":"운영 문서","builtAt":"2026-09-26 09:10","dirty":true}""");
        var a = PortalAbout.Load("test", false, _dir);
        Assert.Equal("b06f185", a.Commit);
        Assert.Equal("운영 문서", a.Subject);
        Assert.Equal("2026-09-26 09:10", a.BuiltAt);
        Assert.True(a.Dirty);
    }

    [Fact]
    public void 깨진_build_info_는_무시한다()
    {
        File.WriteAllText(Path.Combine(_dir, "build-info.json"), "{not json");
        var a = PortalAbout.Load(null, false, _dir);
        Assert.Equal("운영", a.EnvLabel);
        Assert.Equal("", a.Commit);
    }
}
