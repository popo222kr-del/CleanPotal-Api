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
        ImportQuotations(db, Path.Combine(folder, "quotations.json"));
        ImportProductMaster(db, Path.Combine(folder, "product_master.json"));
        ImportGlobalTemplates(db, Path.Combine(folder, "global_templates.json"));
        ImportQuotationConfig(db, Path.Combine(folder, "quotation_config.json"));
        ImportRecipes(db, Path.Combine(folder, "recipes.json"));
        ImportReports(db, Path.Combine(folder, "production_meetings.json"), "meeting");
        ImportReports(db, Path.Combine(folder, "weekly_reports.json"), "weekly");
        ImportBroken(db, Path.Combine(folder, "broken_data.json"));
        ImportNotices(db, Path.Combine(folder, "office_notice.json"));
        ImportSqlite(db, FindDb(folder));

        Console.WriteLine("[import] 완료.");
    }

    /// <summary>SQLite 파일의 테이블·컬럼·행수를 출력한다 (구조 파악용, 설치 도구 불필요).</summary>
    public static void DumpSchema(string dbPath)
    {
        Console.WriteLine($"[schema] DB: {dbPath}");
        if (!File.Exists(dbPath)) { Console.WriteLine("[schema] 파일이 없습니다."); return; }
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            var tables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tables.Add(r.GetString(0));
            }
            Console.WriteLine($"[schema] 테이블 {tables.Count}개: {string.Join(", ", tables)}");

            foreach (var t in tables)
            {
                long count = 0;
                using (var cc = conn.CreateCommand())
                {
                    cc.CommandText = $"SELECT COUNT(*) FROM \"{t}\"";
                    count = Convert.ToInt64(cc.ExecuteScalar() ?? 0L);
                }
                Console.WriteLine($"\n===== {t}  ({count} rows) =====");
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $"PRAGMA table_info(\"{t}\")";
                using var r2 = cmd2.ExecuteReader();
                while (r2.Read())
                    Console.WriteLine($"  - {r2.GetValue(1)} ({r2.GetValue(2)})");
            }
            Console.WriteLine("\n[schema] 완료.");
        }
        catch (Exception ex) { Console.WriteLine($"[schema] 실패: {ex.Message}"); }
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
                // 이미 있으면 새 필드(주소·담당자·즐겨찾기·경로)를 갱신, 없으면 추가
                var e = db.Vendors.FirstOrDefault(x => x.VendorName == v.VendorName);
                bool isNew = e is null;
                e ??= new Vendor();
                e.VendorName = v.VendorName;
                e.Category = string.IsNullOrEmpty(v.Category) ? "일반" : v.Category!;
                e.IsWeekly = v.IsWeekly;
                e.IsFavorite = ToBool(v.IsFavorite);
                e.BasePath = v.BasePath ?? "";
                e.Addresses = Raw(v.Addresses);
                e.Managers = Raw(v.Managers);
                if (isNew) { db.Vendors.Add(e); added++; }
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 업체 {added}개 추가/갱신");
        }
        catch (Exception ex) { Console.WriteLine($"[import] vendors.json 실패: {ex.Message}"); }
    }

    // ── quotations.json (WPF 견적서) ──
    private static void ImportQuotations(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.Quotations.Any()) { Console.WriteLine("[import] 견적: 기존 데이터 있어 건너뜀"); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfQuotation>>(File.ReadAllText(path), Json) ?? new();
            int n = 0;
            foreach (var wq in list)
            {
                var q = new Quotation
                {
                    QuoteNo = wq.QuoteNo ?? "",
                    RfqNo = wq.RfqNo ?? "",
                    Company = wq.Company ?? "",
                    Attention = wq.Attention ?? "",
                    Email = wq.Email ?? "",
                    Phone = wq.Phone ?? "",
                    QuoteDate = D(wq.Date ?? ""),
                    Validity = wq.Validity ?? "",
                    AetsManager = wq.AetsManager ?? "",
                    AetsPhone = wq.AetsPhone ?? "",
                    BusinessNo = wq.BusinessNo ?? "",
                    Remarks = wq.Remarks ?? "",
                    Memo = wq.Memo ?? "",
                    SourceFileName = wq.SourceFileName ?? "",
                    CreatedBy = wq.CreatedBy ?? "import",
                    CreatedAt = DateTime.TryParse(wq.CreatedAt, out var ca) ? ca : DateTime.Now,
                    LastModifiedBy = wq.LastModifiedBy ?? "",
                    LastModifiedAt = DateTime.TryParse(wq.LastModifiedAt, out var ma) ? ma : null,
                };
                int no = 1;
                foreach (var li in wq.LineItems ?? new())
                {
                    q.Items.Add(new QuotationItem
                    {
                        No = li.No > 0 ? li.No : no,
                        Description = li.Description ?? "",
                        PartCode = li.PartCode ?? "",
                        StandardSpec = li.StandardSpec ?? "",
                        ListPrice = li.ListPrice,
                        Qty = li.Qty,
                    });
                    no++;
                }
                db.Quotations.Add(q);
                n++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 견적서 {n}건 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] quotations.json 실패: {ex.Message}"); }
    }

    // ── product_master.json (품목 단가표) ──
    private static void ImportProductMaster(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.ProductMasters.Any()) { Console.WriteLine("[import] 단가표: 기존 데이터 있어 건너뜀"); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfProduct>>(File.ReadAllText(path), Json) ?? new();
            int n = 0;
            foreach (var p in list)
            {
                if (string.IsNullOrWhiteSpace(p.ProductName) && string.IsNullOrWhiteSpace(p.PartCode)) continue;
                db.ProductMasters.Add(new ProductMaster
                {
                    ProductName = p.ProductName ?? "",
                    PartCode = p.PartCode ?? "",
                    Spec = p.Spec ?? "",
                    UnitPrice = p.UnitPrice,
                    VendorName = p.VendorName ?? "",
                    Unit = p.Unit ?? "",
                    UpdatedBy = p.UpdatedBy ?? "import",
                    UpdatedAt = DateTime.TryParse(p.UpdatedAt, out var ua) ? ua : DateTime.Now,
                });
                n++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 품목 단가표 {n}건 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] product_master.json 실패: {ex.Message}"); }
    }

    // ── global_templates.json (전역 품목 템플릿) ──
    private static void ImportGlobalTemplates(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.GlobalTemplates.Any()) { Console.WriteLine("[import] 전역템플릿: 기존 데이터 있어 건너뜀"); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfTemplate>>(File.ReadAllText(path), Json) ?? new();
            int n = 0;
            foreach (var t in list)
            {
                if (string.IsNullOrWhiteSpace(t.ProductCode) && string.IsNullOrWhiteSpace(t.ProductName)) continue;
                db.GlobalTemplates.Add(new GlobalTemplate
                {
                    ProductCode = t.ProductCode ?? "",
                    ProductName = t.ProductName ?? "",
                    TemplatePath = t.TemplatePath ?? "",
                });
                n++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 전역 템플릿 {n}건 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] global_templates.json 실패: {ex.Message}"); }
    }

    // ── quotation_config.json (견적 설정) ──
    private static void ImportQuotationConfig(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.QuotationConfigs.Any()) return;
        try
        {
            var cfg = JsonSerializer.Deserialize<WpfQuoteConfig>(File.ReadAllText(path), Json);
            if (cfg is null) return;
            db.QuotationConfigs.Add(new QuotationConfig { BusinessNo = cfg.BusinessNo ?? "" });
            db.SaveChanges();
            Console.WriteLine("[import] 견적 설정 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] quotation_config.json 실패: {ex.Message}"); }
    }

    // ── recipes.json (세정 레시피) ──
    private static void ImportRecipes(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.Recipes.Any()) { Console.WriteLine("[import] 레시피: 기존 데이터 있어 건너뜀"); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfRecipe>>(File.ReadAllText(path), Json) ?? new();
            int n = 0;
            foreach (var r in list)
            {
                db.Recipes.Add(new Recipe
                {
                    Text = r.Text ?? "",
                    DisplayText = r.DisplayText ?? "",
                    S2Minutes = ToDouble(r.S2Minutes),
                    S2Temperature = ToDouble(r.S2Temperature),
                    HfMinutes = ToDouble(r.HfMinutes),
                    DiMinutes = ToDouble(r.DiMinutes),
                    TotalMinutes = ToDouble(r.TotalMinutes),
                    IsFavorite = ToBool(r.IsFavorite),
                    OrderIndex = r.OrderIndex,
                });
                n++;
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 레시피 {n}건 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] recipes.json 실패: {ex.Message}"); }
    }

    // ── production_meetings.json / weekly_reports.json (회의록·보고서) ──
    private static void ImportReports(CleanPotalDbContext db, string path, string type)
    {
        if (!File.Exists(path)) return;
        if (db.Reports.Any(r => r.ReportType == type)) { Console.WriteLine($"[import] 보고서({type}): 기존 데이터 있어 건너뜀"); return; }
        try
        {
            var groups = JsonSerializer.Deserialize<List<WpfReportGroup>>(File.ReadAllText(path), Json) ?? new();
            int order = 0, n = 0;
            foreach (var g in groups)
            {
                foreach (var rep in g.Reports ?? new())
                {
                    var r = new Report
                    {
                        ReportType = type,
                        MonthTitle = g.MonthTitle ?? "",
                        Title = rep.Title ?? "",
                        ShortTitle = rep.ShortTitle ?? "",
                        DateRange = rep.DateRange ?? "",
                        Memo = rep.Memo ?? "",
                        MemoRich = rep.MemoRich ?? "",
                        MainContent = rep.MainContent ?? "",
                        MainContentRich = rep.MainContentRich ?? "",
                        NightContent = rep.NightContent ?? "",
                        NightContentRich = rep.NightContentRich ?? "",
                        Attendees = rep.Attendees ?? "",
                        Summary = rep.Summary ?? "",
                        MemoAttachments = Raw(rep.MemoAttachments),
                        MainAttachments = Raw(rep.MainAttachments),
                        SortOrder = order++,
                    };
                    foreach (var b in rep.Blocks ?? new())
                    {
                        r.Blocks.Add(new ReportBlock
                        {
                            Number = b.Number,
                            Category = b.Category ?? "",
                            Status = b.Status ?? "",
                            Content = b.Content ?? "",
                            ContentRich = b.ContentRich ?? "",
                            FollowUp = b.FollowUp ?? "",
                            FollowUpRich = b.FollowUpRich ?? "",
                            Kind = b.Kind?.ToString() ?? "",
                            Heading = b.Heading ?? "",
                            IsCollapsed = b.IsCollapsed,
                            ProgressPercent = (int)(b.ProgressPercent ?? 0),
                            Importance = b.Importance?.ToString() ?? "",
                            FollowUpAttachments = Raw(b.FollowUpAttachments),
                        });
                    }
                    db.Reports.Add(r);
                    n++;
                }
            }
            db.SaveChanges();
            Console.WriteLine($"[import] 보고서({type}) {n}건 추가");
        }
        catch (Exception ex) { Console.WriteLine($"[import] {Path.GetFileName(path)} 실패: {ex.Message}"); }
    }

    // JsonElement 배열/객체를 원본 JSON 문자열로 보존 (첨부 목록 등)
    private static string Raw(JsonElement? e)
        => e is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } v ? v.GetRawText() : "";

    // 숫자/숫자문자열 어느 쪽이든 double 로 변환 (아니면 0)
    private static double ToDouble(JsonElement? e)
    {
        if (e is not { } v) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d : 0,
            JsonValueKind.String => double.TryParse(v.GetString(), out var d2) ? d2 : 0,
            _ => 0,
        };
    }

    // bool 이 아닌 값(0/1, "공식"/"true"/"Y" 등)도 관대하게 bool 로 변환
    private static bool ToBool(JsonElement? e)
    {
        if (e is not { } v) return false;
        switch (v.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return v.TryGetDouble(out var d) && d != 0;
            case JsonValueKind.String:
                var s = (v.GetString() ?? "").Trim().ToLowerInvariant();
                return s is "true" or "1" or "y" or "yes" or "t" or "공식" or "official" or "o";
            default: return false;
        }
    }

    // ── broken_data.json (파손 기록 + 교육 기록 + 목표 + 메모) ──
    private static void ImportBroken(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonSerializer.Deserialize<WpfBrokenData>(File.ReadAllText(path), Json);
            if (data is null) return;

            if (!db.BrokenRecords.Any())
            {
                int n = 0;
                foreach (var r in data.Records ?? new())
                {
                    db.BrokenRecords.Add(new BrokenRecord
                    {
                        OccurDate = D(r.OccurDate ?? ""),
                        Line = r.Line ?? "", ProductName = r.ProductName ?? "", ProductType = r.ProductType ?? "",
                        SN = r.SN ?? "", Team = r.Team ?? "", Causer = r.Causer ?? "",
                        JobTitle = r.JobTitle ?? "", Career = r.Career ?? "", OccurStage = r.OccurStage ?? "",
                        Status = string.IsNullOrEmpty(r.Status) ? "접수" : r.Status!,
                        IsOfficial = ToBool(r.IsOfficial), PositionFrozen = ToBool(r.PositionFrozen),
                        IncidentReports = Raw(r.IncidentReports), CountermeasureReports = Raw(r.CountermeasureReports),
                        TrainingDocs = Raw(r.TrainingDocs), TrainingImages = Raw(r.TrainingImages),
                    });
                    n++;
                }
                Console.WriteLine($"[import] 파손 기록 {n}건 추가");
            }

            if (!db.BrokenTrainings.Any())
            {
                int order = 0, n = 0;
                void AddT(List<WpfTraining>? list, string type)
                {
                    foreach (var t in list ?? new())
                    {
                        db.BrokenTrainings.Add(new BrokenTraining
                        {
                            TrainingType = type, TrainingDate = D(t.TrainingDate ?? ""),
                            Content = t.Content ?? "", Documents = Raw(t.Documents), Images = Raw(t.Images),
                            SortOrder = order++,
                        });
                    }
                }
                AddT(data.TrainingRecordsProduction, "production");
                AddT(data.TrainingRecordsLogistics, "logistics");
                n = (data.TrainingRecordsProduction?.Count ?? 0) + (data.TrainingRecordsLogistics?.Count ?? 0);
                Console.WriteLine($"[import] 교육 기록 {n}건 추가");
            }

            if (!db.BrokenGoals.Any() && data.TrainingGoals is not null)
            {
                void AddG(Dictionary<string, JsonElement>? targets, string cat)
                {
                    foreach (var kv in targets ?? new())
                        if (int.TryParse(kv.Key, out var year))
                            db.BrokenGoals.Add(new BrokenGoal { Category = cat, Year = year, Target = kv.Value.ToString() });
                }
                AddG(data.TrainingGoals.ProductionTargets, "production");
                AddG(data.TrainingGoals.LogisticsTargets, "logistics");
            }

            if (!db.BrokenMetas.Any() && !string.IsNullOrEmpty(data.Memo))
                db.BrokenMetas.Add(new BrokenMeta { Memo = data.Memo! });

            db.SaveChanges();
            Console.WriteLine("[import] BROKEN 완료");
        }
        catch (Exception ex) { Console.WriteLine($"[import] broken_data.json 실패: {ex.Message}"); }
    }

    // ── office_notice.json (사무실 공지) — 현재는 빈 배열, 데이터 생기면 관대 매핑 ──
    private static void ImportNotices(CleanPotalDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        if (db.Notices.Any()) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<WpfNotice>>(File.ReadAllText(path), Json) ?? new();
            int n = 0;
            foreach (var x in list)
            {
                var content = x.Content ?? x.Text ?? x.Body ?? "";
                var title = x.Title ?? "";
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content)) continue;
                db.Notices.Add(new Notice
                {
                    Title = title,
                    Content = content,
                    Author = x.Author ?? x.Writer ?? "import",
                    CreatedAt = DateTime.TryParse(x.Date ?? x.CreatedAt, out var d) ? d : DateTime.Now,
                });
                n++;
            }
            if (n > 0) { db.SaveChanges(); Console.WriteLine($"[import] 공지 {n}건 추가"); }
        }
        catch (Exception ex) { Console.WriteLine($"[import] office_notice.json 실패: {ex.Message}"); }
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
        public JsonElement? IsFavorite { get; set; }
        public string? BasePath { get; set; }
        public JsonElement? Addresses { get; set; }
        public JsonElement? Managers { get; set; }
    }
    private class WpfQuotation
    {
        public string? QuoteNo { get; set; }
        public string? RfqNo { get; set; }
        public string? Company { get; set; }
        public string? Attention { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Date { get; set; }
        public string? Validity { get; set; }
        public string? AetsManager { get; set; }
        public string? AetsPhone { get; set; }
        public string? BusinessNo { get; set; }
        public string? Remarks { get; set; }
        public string? Memo { get; set; }
        public string? SourceFileName { get; set; }
        public string? CreatedBy { get; set; }
        public string? CreatedAt { get; set; }
        public string? LastModifiedBy { get; set; }
        public string? LastModifiedAt { get; set; }
        public List<WpfLineItem>? LineItems { get; set; }
    }
    private class WpfLineItem
    {
        public int No { get; set; }
        public string? Description { get; set; }
        public string? PartCode { get; set; }
        public string? StandardSpec { get; set; }
        public decimal ListPrice { get; set; }
        public decimal Qty { get; set; }
    }
    private class WpfProduct
    {
        public string? ProductName { get; set; }
        public string? PartCode { get; set; }
        public string? Spec { get; set; }
        public decimal UnitPrice { get; set; }
        public string? VendorName { get; set; }
        public string? Unit { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedAt { get; set; }
    }
    private class WpfTemplate
    {
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? TemplatePath { get; set; }
    }
    private class WpfQuoteConfig
    {
        public string? BusinessNo { get; set; }
    }
    private class WpfRecipe
    {
        public string? Text { get; set; }
        public string? DisplayText { get; set; }
        public JsonElement? S2Minutes { get; set; }
        public JsonElement? S2Temperature { get; set; }
        public JsonElement? HfMinutes { get; set; }
        public JsonElement? DiMinutes { get; set; }
        public JsonElement? TotalMinutes { get; set; }
        public JsonElement? IsFavorite { get; set; }
        public int OrderIndex { get; set; }
    }
    private class WpfReportGroup
    {
        public string? MonthTitle { get; set; }
        public List<WpfReport>? Reports { get; set; }
    }
    private class WpfReport
    {
        public string? Title { get; set; }
        public string? ShortTitle { get; set; }
        public string? DateRange { get; set; }
        public string? Memo { get; set; }
        public string? MemoRich { get; set; }
        public string? MainContent { get; set; }
        public string? MainContentRich { get; set; }
        public string? NightContent { get; set; }
        public string? NightContentRich { get; set; }
        public string? Attendees { get; set; }
        public string? Summary { get; set; }
        public JsonElement? MemoAttachments { get; set; }
        public JsonElement? MainAttachments { get; set; }
        public List<WpfBlock>? Blocks { get; set; }
    }
    private class WpfBlock
    {
        public int Number { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string? Content { get; set; }
        public string? ContentRich { get; set; }
        public string? FollowUp { get; set; }
        public string? FollowUpRich { get; set; }
        public object? Kind { get; set; }
        public string? Heading { get; set; }
        public bool IsCollapsed { get; set; }
        public double? ProgressPercent { get; set; }
        public object? Importance { get; set; }
        public JsonElement? FollowUpAttachments { get; set; }
    }
    private class WpfBrokenData
    {
        public List<WpfBrokenRecord>? Records { get; set; }
        public string? Memo { get; set; }
        public List<WpfTraining>? TrainingRecordsProduction { get; set; }
        public List<WpfTraining>? TrainingRecordsLogistics { get; set; }
        public WpfTrainingGoals? TrainingGoals { get; set; }
    }
    private class WpfBrokenRecord
    {
        public int No { get; set; }
        public string? OccurDate { get; set; }
        public string? Line { get; set; }
        public string? ProductName { get; set; }
        public string? SN { get; set; }
        public string? Team { get; set; }
        public string? Causer { get; set; }
        public string? JobTitle { get; set; }
        public string? Career { get; set; }
        public JsonElement? PositionFrozen { get; set; }
        public string? ProductType { get; set; }
        public string? OccurStage { get; set; }
        public string? Status { get; set; }
        public JsonElement? IsOfficial { get; set; }
        public JsonElement? IncidentReports { get; set; }
        public JsonElement? CountermeasureReports { get; set; }
        public JsonElement? TrainingDocs { get; set; }
        public JsonElement? TrainingImages { get; set; }
    }
    private class WpfTraining
    {
        public string? TrainingDate { get; set; }
        public string? Content { get; set; }
        public JsonElement? Documents { get; set; }
        public JsonElement? Images { get; set; }
    }
    private class WpfTrainingGoals
    {
        public Dictionary<string, JsonElement>? ProductionTargets { get; set; }
        public Dictionary<string, JsonElement>? LogisticsTargets { get; set; }
    }
    private class WpfNotice
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Text { get; set; }
        public string? Body { get; set; }
        public string? Author { get; set; }
        public string? Writer { get; set; }
        public string? Date { get; set; }
        public string? CreatedAt { get; set; }
    }
}
