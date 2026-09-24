using System.Net;
using System.Net.Sockets;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// MES 는 포털 JWT 를 "포털 주소/api/auth/me" 로 확인한다. 그 주소를 브라우저가 보낸 Host 헤더로
/// 만들면, Host 를 자기 PC 로 바꾼 요청 한 번으로 MES 관리자 쿠키를 받아 갈 수 있다.
/// 그래서 프록시(YARP)가 포털이 실제로 요청을 받은 IP:포트를 이 헤더로 따로 알려 주고,
/// 클라이언트가 같은 이름으로 보낸 값은 지운다. MES 는 루프백에서 온 이 값만 믿는다.
/// </summary>
public static class MesPortalEndpoint
{
    public const string HeaderName = "X-CleanPotal-Portal-Endpoint";

    public static string? From(HttpContext http)
    {
        var ip = http.Connection.LocalIpAddress;
        var port = http.Connection.LocalPort;
        if (ip is null || port <= 0) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return null;

        // IPv6 는 대괄호로 감싸고, URL 에 쓸 수 없는 범위 ID(%n)는 뗀다.
        var host = ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{new IPAddress(ip.GetAddressBytes())}]"
            : ip.ToString();
        return $"{http.Request.Scheme}://{host}:{port}";
    }
}
