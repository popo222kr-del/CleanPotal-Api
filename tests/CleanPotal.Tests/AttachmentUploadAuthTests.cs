using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 첨부 올리기 권한. 현장 점검(field) 첨부만 조회 등급이 올린다 — 체크시트 NG·작업 전후 사진을
/// 조회 등급 생산직이 QR 점검 중에 찍는다. 다른 영역은 어디든 편집 등급이어야 한다.
/// </summary>
[Collection(PortalAppCollection.Name)]
public class AttachmentUploadAuthTests
{
    private readonly PortalAppFixture _app;
    public AttachmentUploadAuthTests(PortalAppFixture app) => _app = app;

    private static MultipartFormDataContent Jpeg()
    {
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "files", "ng.jpg" } };
    }

    [Fact]
    public async Task 현장_조회_등급은_field_사진을_올린다()
    {
        using var client = await _app.SignInAsync("field-view");
        var res = await client.PostAsync("/api/attachments?scope=field", Jpeg());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reports")]
    public async Task 현장_조회_등급은_다른_영역에는_못_올린다(string scope)
    {
        using var client = await _app.SignInAsync("field-view");
        var res = await client.PostAsync($"/api/attachments?scope={scope}", Jpeg());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task 현장_권한이_없으면_field_사진도_못_올린다()
    {
        using var client = await _app.SignInAsync("mes-view");
        var res = await client.PostAsync("/api/attachments?scope=field", Jpeg());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task 편집_등급은_영역_없이도_올린다()
    {
        using var client = await _app.SignInAsync("field-only");
        var res = await client.PostAsync("/api/attachments", Jpeg());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
