using CleanPotal.Core.Entities;
using CleanPotal.Core.Iot;
using CleanPotal.Infrastructure.Data;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 설정(Zigbee:Sensors)에 적어 둔 센서를 표에 심는다. <b>없는 것만 추가</b>하고, 이미 있으면 손대지 않는다.
///
/// 표가 기준인 이유는 나중에 관리자 화면에서 이름·사용 여부를 고치기 때문이다. 설정은 "처음 한 번" 을
/// 편하게 하려는 것뿐이라, 표에서 고친 값을 다시 덮어쓰면 안 된다.
/// </summary>
public static class ZigbeeSensorSeeder
{
    public static void Run(CleanPotalDbContext db, ZigbeeOptions options)
    {
        if (options.Sensors.Count == 0) return;

        var existing = db.ZigbeeSensors.Select(s => s.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        var order = 0;
        var added = 0;

        foreach (var s in options.Sensors)
        {
            order++;
            var id = (s.DeviceId ?? "").Trim();
            if (id.Length == 0 || existing.Contains(id)) continue;

            db.ZigbeeSensors.Add(new ZigbeeSensor
            {
                DeviceId = id,
                Site = (s.Site ?? "").Trim(),
                DisplayName = string.IsNullOrWhiteSpace(s.Name) ? id : s.Name.Trim(),
                IsEnabled = true,
                SortOrder = order,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added == 0) return;
        db.SaveChanges();
        Console.WriteLine($"[zigbee] 센서 마스터 {added}건 추가(설정에서 심음).");
    }
}
