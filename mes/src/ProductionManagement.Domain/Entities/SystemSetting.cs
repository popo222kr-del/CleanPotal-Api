namespace ProductionManagement.Domain.Entities;

// 장기대기 기준시간 등 코드에 하드코딩하면 안 되는 값을 담는다 (CLAUDE.md 7번).
public class SystemSetting : Entity<int>
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string? Description { get; set; }
}
