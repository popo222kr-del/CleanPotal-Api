using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 상태 점검 — GET /api/health (로그인 없이). 사이트·DB·온습도 수집이 살아 있는지 한 번에 본다.
///
/// 9/26 새벽처럼 앱이 멈추거나 센서 수집이 끊겨도 사람이 화면을 열어 보기 전까지 몰랐다.
/// 예약 작업·모니터링 도구가 이 주소만 주기적으로 부르면 된다: 정상 200, 문제 있으면 503.
/// 값은 켜짐/꺼짐과 경과 분만 준다(센서 값·이름·설정은 주지 않는다).
/// </summary>
public static class PortalHealth
{
    /// <summary>마지막 센서 수신이 이보다 오래되면 수집 끊김으로 본다.</summary>
    public const int StaleMinutes = 30;

    public sealed record Report(bool Ok, string Db, bool MqttEnabled, bool MqttConnected,
        int? LastReadingMinutesAgo, string[] Problems, DateTime At);

    public static async Task<Report> CheckAsync(CleanPotalDbContext db, ZigbeeSensorStore store, ZigbeeOptions zigbee, DateTime now, CancellationToken ct)
    {
        var problems = new List<string>();
        var dbState = "ok";
        try { await db.Database.ExecuteSqlRawAsync("SELECT 1", ct); }
        catch (Exception) { dbState = "error"; problems.Add("DB 에 접속할 수 없습니다"); }

        var mqttOn = zigbee.Mqtt.Enabled;
        int? ago = store.NewestReceivedAt is { } last ? (int)Math.Max(0, (now - last).TotalMinutes) : null;
        if (mqttOn)
        {
            if (!store.MqttConnected) problems.Add("온습도 수집(MQTT 브로커)에 연결돼 있지 않습니다");
            else if (ago is null || ago > StaleMinutes) problems.Add($"온습도 센서 값이 {StaleMinutes}분 넘게 들어오지 않았습니다");
        }
        return new Report(problems.Count == 0, dbState, mqttOn, store.MqttConnected, ago, problems.ToArray(), now);
    }
}
