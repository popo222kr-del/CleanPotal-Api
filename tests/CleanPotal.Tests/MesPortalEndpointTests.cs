using System.Net;
using CleanPotal.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// MES 가 토큰 확인에 쓸 포털 주소는 브라우저가 보낸 Host 가 아니라 포털이 실제로 요청을 받은 IP:포트여야 한다.
/// Host 를 믿으면 Host 를 자기 PC 로 바꾼 요청 한 번으로 MES 관리자 쿠키를 받아 갈 수 있었다.
/// </summary>
public class MesPortalEndpointTests
{
    private static DefaultHttpContext Ctx(IPAddress? local, int port, string host = "attacker.example:9999")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString(host);
        ctx.Connection.LocalIpAddress = local;
        ctx.Connection.LocalPort = port;
        return ctx;
    }

    [Fact]
    public void 요청의_Host_가_아니라_실제_수신_주소를_쓴다()
        => Assert.Equal("http://10.10.10.119:8713", MesPortalEndpoint.From(Ctx(IPAddress.Parse("10.10.10.119"), 8713)));

    [Fact]
    public void IPv6_는_대괄호로_감싼다()
        => Assert.Equal("http://[::1]:8713", MesPortalEndpoint.From(Ctx(IPAddress.IPv6Loopback, 8713)));

    [Fact]
    public void IPv4_매핑_주소는_IPv4_로_적는다()
        => Assert.Equal("http://127.0.0.1:5001",
            MesPortalEndpoint.From(Ctx(IPAddress.Parse("::ffff:127.0.0.1"), 5001)));

    [Fact]
    public void 수신_주소를_모르면_알려_주지_않는다()
    {
        Assert.Null(MesPortalEndpoint.From(Ctx(null, 8713)));
        Assert.Null(MesPortalEndpoint.From(Ctx(IPAddress.Any, 8713)));
        Assert.Null(MesPortalEndpoint.From(Ctx(IPAddress.Loopback, 0)));
    }
}
