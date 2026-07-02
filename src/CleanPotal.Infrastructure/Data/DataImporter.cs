using System.Text.Json;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 기존 WPF 데이터(users.json / buttons.json / vendors.json / cleanpotal.db)를
/// 새 EF Core DB로 옮긴다. 윈도우 PC에서 `dotnet run -- import [폴더]` 로 실행.
/// 존재하는 파일만 처리하고, 각 단계는 독립적으로 try/catch 한다.
/// </summary>
public static class DataImporter
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static void Run(CleanPotalDbContext db, string folder)
    {
        Console.WriteLine($"[import] 폴더: {folder}");
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"[import] 폴더가 없습니다: {folder}");
            return;
        }

        ImportUsers(db, Path.Combine(folder, "users.json"));
        ImportButtons(db, Path.Combine(folder, "buttons.json"));
        ImportVendors(db, Path.Combine(folder, "vendors.json"));
        ImportSqlite(db, FindDb(folder));

        Console.WriteLine("[import] 완료.");
    }

    private static string? FindDb(string folder)
    {
        foreach (var name in new[] { "cleanpotal.db", "dispatch.db", "CleanPotal.db" })
        {
            var p = Path.Combine(folder, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // ── users.json (WPF UserModel) ──
    private static void ImportUsers(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfUser>>(File.ReadAllText(path), Json) ?? new();
            int added = 0;
            foreach (var u in list)
            {
                if (string.IsNullOrWhiteSpace(u.Username)) continue;
                if (db.Users.Any(x => x.Username == u.Username)) continue;
                db.Users.Add(new User
                {
                    Username = u.Username,
                    // WPF는 평문 비번 → 해시. 비어있으면 기본 1234
                    PasswordHash = PasswordHasher.Hash(string.IsNullOrEmpty(u.Password) ? "1234" : u.Password),
                    RealName = u.RealName ?? "",
                    TeamName = u.TeamName ?? "",
                    JobTitle = u.JobTitle ?? "",
                    Email = u.Email ?? "",
                    PhoneNumber = u.PhoneNumber ?? "",
                    EmployeeNumber = string.IsNullOrEmpty(u.EmployeeNumber) ? u.Username : u.EmployeeNumber,
                    HireDate = u.HireDate ?? "",
                    IsResigned = u.IsResigned,
                    ResignDate = u.ResignDate ?? "",
                    IsAdmin = u.Username == "1004",
                    CanManageFiles = u.CanManageFiles,
                    CanManageNotices = u.CanManageNotices,
                    CanManageVendors = u.CanManageVendors,
                    CanManageSchedule = u.CanManageSchedule,
                    CanManageBroken = u.CanManageBroken,
                    CanAccessEtcMenu = u.CanAccessEtcMenu,
                    CanManageShiftBoard = u.CanManageShiftBoard,
                    CanManageInventory = u.CanManageInventory,
                });
                added++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 사용자 {added}명 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] users.json 실패: {ex.Message}"); }
    }

    // ── buttons.json (WPF 포탈 ButtonGroup) ──
    private static void ImportButtons(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var groups = JsonSerializer.Deserialize<List<WpfButtonGroup>>(File.ReadAllText(path), Json) ?? new();
            int g = 0, it = 0, order = 0;
            foreach (var grp in groups)
            {
                if (string.IsNullOrWhiteSpace(grp.Group)) continue;
                if (db.PortalGroups.Any(x => x.Name == grp.Group)) continue;
                var pg = new PortalGroup { Name = grp.Group, SortOrder = ++order };
                int io = 0;
                foreach (var item in grp.Items ?? new())
                {
                    pg.Items.Add(new PortalItem
                    {
                        Title = item.Title ?? "",
                        Path = item.Path ?? "",
                        Type = string.IsNullOrEmpty(item.Type) ? "folder" : item.Type,
                        SortOrder = ++io,
                    });
                    it++;
                }
                db.PortalGroups.Add(pg);
                g++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 포탈 그룹 {g}개 / 항목 {it}개 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] buttons.json 실패: {ex.Message}"); }
    }

    // ── vendors.json (WPF VendorModel) ──
    private static void ImportVendors(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfVendor>>(File.ReadAllText(path), Json) ?? new();
            int added = 0;
            foreach (var v in list)
            {
                if (string.IsNullOrWhiteSpace(v.VendorName)) continue;
                if (db.Vendors.Any(x => x.VendorName == v.VendorName)) continue;
                db.Vendors.Add(new Vendor
                {
                    VendorName = v.VendorName,
                    Category = string.IsNullOrEmpty(v.Category) ? "일반" : v.Category,
                    IsWeekly = v.IsWeekly,
                });
                added++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 업체 {added}개 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] vendors.json 실패: {ex.Message}"); }
    }

    // ── WPF SQLite (HandoverList / ShiftSchedule / TeamEvents) ──
    private static void ImportSqlite(CleanPotalDbContext db, string? dbPath)
    {
        if (dbPath is null) return;
        Console.WriteLine($"[import] WPF DB: {dbPath}");
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            ImportHandovers(db, conn);
            ImportShifts(db, conn);
            ImportTeamEvents(db, conn);
        }
        catch (Exception ex) { Console.WriteLine($"[import] SQLite 실패: {ex.Message}"); }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static string S(SqliteDataReader r, string col)
    {
        try { int i = r.GetOrdinal(col); return r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? ""; }
        catch { return ""; }
    }

    private static DateOnly? D(string s)
        => DateTime.TryParse(s, out var dt) ? DateOnly.FromDateTime(dt) : null;

    private static void ImportHandovers(CleanPotalDbContext db, SqliteConnection conn)
    {
        if (!TableExists(conn, "HandoverList")) return;
        if (db.Handovers.Any()) { Console.WriteLine("[import] 인수인계: 기존 데이터 있어 건너뜀"); return; }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HandoverList";
        using var r = cmd.ExecuteReader();
        int n = 0;
        while (r.Read())
        {
            db.Handovers.Add(new Handover
            {
                Vendor = S(r, "Vendor"),
                Category = S(r, "Category") is { Length: > 0 } c ? c : "QTZ",
                Owner = S(r, "Owner"),
                Content = S(r, "Content"),
                InDate = D(S(r, "InDate")),
                OutDate = D(S(r, "OutDate")),
                Status = S(r, "Status") is { Length: > 0 } s ? s : "진행",
                DeliveryMethod = S(r, "DeliveryMethod"),
                Memo = S(r, "Memo"),
                CreatorName = S(r, "CreatorName"),
            });
            n++;
        }
        db.SaveChanges();
        Console.WriteLine($"[import] 인수인계 {n}건 추가");
    }

    private static void ImportShifts(CleanPotalDbContext db, SqliteConnection conn)
    {
        if (!TableExists(conn, "ShiftSchedule")) return;
        if (db.ShiftSchedules.Any()) { Console.WriteLine("[import] 근무: 기존 데이터 있어 건너뜀"); return; }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ShiftSchedule";
        using var r = cmd.ExecuteReader();
        int n = 0;
        while (r.Read())
        {
            var date = D(S(r, "TargetDate"));
            if (date is null) continue;
            db.ShiftSchedules.Add(new ShiftSchedule
            {
                MemberName = S(r, "MemberName"),
                TargetDate = date.Value,
                ShiftType = S(r, "ShiftType"),
                TeamGroup = S(r, "TeamGroup"),
                CreatorName = "import",
            });
            n++;
        }
        db.SaveChanges();
        Console.WriteLine($"[import] 근무 {n}건 추가");
    }

    private static void ImportTeamEvents(CleanPotalDbContext db, SqliteConnection conn)
    {
        if (!TableExists(conn, "TeamEvents")) return;
        if (db.TeamEvents.Any()) { Console.WriteLine("[import] 팀일정: 기존 데이터 있어 건너뜀"); return; }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TeamEvents";
        using var r = cmd.ExecuteReader();
        int n = 0;
        while (r.Read())
        {
            var s = D(S(r, "StartDate"));
            var e = D(S(r, "EndDate"));
            if (s is null) continue;
            db.TeamEvents.Add(new TeamEvent
            {
                RegisteredBy = S(r, "RegisteredBy"),
                StartDate = s.Value,
                EndDate = e ?? s.Value,
                Content = S(r, "Content"),
                Detail = S(r, "Detail"),
            });
            n++;
        }
        db.SaveChanges();
        Console.WriteLine($"[import] 팀일정 {n}건 추가");
    }

    // ── WPF 파일 형식 매핑용 DTO ──
    private class WpfUser
    {
        public string Username { get; set; } = "";
        public string? Password { get; set; }
        public string? RealName { get; set; }
        public string? TeamName { get; set; }
        public string? JobTitle { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? EmployeeNumber { get; set; }
        public string? HireDate { get; set; }
        public bool IsResigned { get; set; }
        public string? ResignDate { get; set; }
        public bool CanManageFiles { get; set; }
        public bool CanManageNotices { get; set; }
        public bool CanManageVendors { get; set; }
        public bool CanManageSchedule { get; set; }
        public bool CanManageBroken { get; set; }
        public bool CanAccessEtcMenu { get; set; }
        public bool CanManageShiftBoard { get; set; }
        public bool CanManageInventory { get; set; }
    }
    private class WpfButtonGroup
    {
        public string Group { get; set; } = "";
        public List<WpfButtonItem>? Items { get; set; }
    }
    private class WpfButtonItem
    {
        public string? Title { get; set; }
        public string? Path { get; set; }
        public string? Type { get; set; }
    }
    private class WpfVendor
    {
        public string VendorName { get; set; } = "";
        public string? Category { get; set; }
        public bool IsWeekly { get; set; }
    }
}
