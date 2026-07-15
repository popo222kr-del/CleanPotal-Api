using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;

namespace CleanPotal.Infrastructure.Data;

/// <summary>초기 시드 데이터 (마이그레이션 도입 전 개발용).</summary>
public static class DbSeeder
{
    public static void Seed(CleanPotalDbContext db)
    {
        SeedInspection(db);       // 점검 항목 (실제 연동 전 기본값)
        SeedScheduleRecipes(db);  // 스케줄보드 기본 레시피 (WPF SeedRecipes)
        SeedScheduleEquipments(db); // 스케줄보드 설비 19대 (하드코딩 → DB)
        SeedInventory(db);          // 현장 재고 34품목 (WPF FieldInventory)
        NormalizeScheduleEquipments(db); // 기존 통합 Name → 설비명/공정/특이사항 분리 (재임포트 없이 적용)
        NormalizeAdmins(db);      // 최고관리자 직급 → 관리자 권한 보정 (기존 DB에 재임포트 없이 적용)
        if (db.Users.Any()) return;

        // 로그인 보장용 최고관리자(1004)만 시드. 실제 사용자는 import(dispatch.db Users)가 채운다.
        db.Users.Add(new User
        {
            Username = "1004",
            PasswordHash = PasswordHasher.Hash("1234"),
            RealName = "박주언",
            TeamName = "Office",
            JobTitle = "대리",
            EmployeeNumber = "1004",
            IsAdmin = true,
            CanManageFiles = true,
            CanManageNotices = true,
            CanManageVendors = true,
            CanManageSchedule = true,
            CanManageBroken = true,
            CanAccessEtcMenu = true,
            CanManageShiftBoard = true,
            CanManageInventory = true,
        });
        db.SaveChanges();
    }

    /// <summary>직급이 최고관리자인데 IsAdmin이 꺼져 있는 계정 보정 (예: AETS).</summary>
    private static void NormalizeAdmins(CleanPotalDbContext db)
    {
        var fixTargets = db.Users
            .Where(u => !u.IsAdmin && u.JobTitle.Contains("최고관리자"))
            .ToList();
        if (fixTargets.Count == 0) return;
        foreach (var u in fixTargets) u.IsAdmin = true;
        db.SaveChanges();
        Console.WriteLine($"[seed] 최고관리자 권한 보정 {fixTargets.Count}건: {string.Join(", ", fixTargets.Select(u => u.Username))}");
    }

    /// <summary>스케줄보드 기본 레시피 (WPF SeedRecipes: 0-15-100, 120-30-100@60, 30-30-100).</summary>
    private static void SeedScheduleRecipes(CleanPotalDbContext db)
    {
        if (db.ScheduleRecipes.Any()) return;
        (int s2, int hf, int di, int? t, string text)[] defaults =
        {
            (0, 15, 100, null, "0-15-100"),
            (120, 30, 100, 60, "120-30-100@60"),
            (30, 30, 100, null, "30-30-100"),
        };
        int i = 0;
        foreach (var d in defaults)
            db.ScheduleRecipes.Add(new ScheduleRecipe
            {
                Text = d.text, S2Minutes = d.s2, HFMinutes = d.hf, DIMinutes = d.di,
                S2Temperature = d.t, OrderIndex = i++,
            });
        db.SaveChanges();
    }

    /// <summary>스케줄보드 설비 19대 시드 (WPF SeedEquipments). Slot=순번(블록 참조), 비면 채운다.</summary>
    private static void SeedScheduleEquipments(CleanPotalDbContext db)
    {
        if (db.ScheduleEquipments.Any()) return;
        string[] names =
        {
            "MDC01 (POLY)", "MDC02 (Hot Chemical)", "MDC03 (Hot Chemical)", "MDC04 (POLY)", "MDC05 (TEOS)",
            "MDC06 (ALO/HFO)", "MDC07 (POLY)", "MDC08 (N,G,D-POLY)", "MDC09 (SIGE)", "MDC10 (ALO/HFO)",
            "MSC01-1 (POLY/대대배치)", "MSC01-2 (Rinse 전용)",
            "NDC01 (WOOAM)", "NDC02 (OXIDE)", "NDC03 (A급)", "NDC04 (A급)", "NDC05 (N,G,D-POLY)",
            "NDC06 (Hot Chemical)", "NDC07 (SiN)",
        };
        for (int i = 0; i < names.Length; i++)
        {
            var g = names[i].StartsWith("MDC") ? "MDC" : names[i].StartsWith("MSC") ? "MSC" : "NDC";
            var (nm, proc, note) = ParseEquipName(names[i]);
            db.ScheduleEquipments.Add(new ScheduleEquipment
            {
                Name = nm, Process = proc, Note = note,
                GroupName = g, Slot = i, OrderIndex = i, IsActive = true,
            });
        }
        db.SaveChanges();
        Console.WriteLine($"[seed] 스케줄보드 설비 {names.Length}대 시드");
    }

    /// <summary>통합 이름("MDC02 (Hot Chemical)")을 설비명/공정/특이사항으로 분리. 괄호 없는 행은 그대로 둔다(멱등).</summary>
    private static void NormalizeScheduleEquipments(CleanPotalDbContext db)
    {
        var rows = db.ScheduleEquipments.Where(e => e.Name.Contains("(")).ToList();
        if (rows.Count == 0) return;
        foreach (var e in rows)
        {
            var (nm, proc, note) = ParseEquipName(e.Name);
            e.Name = nm;
            if (string.IsNullOrEmpty(e.Process)) e.Process = proc;
            if (string.IsNullOrEmpty(e.Note)) e.Note = note;
        }
        db.SaveChanges();
        Console.WriteLine($"[normalize] 스케줄보드 설비 {rows.Count}대 이름 분리(설비명/공정/특이사항)");
    }

    /// <summary>"MDC02 (POLY) (Hot Chemical)" → (MDC02, POLY, Hot Chemical). 첫 괄호=공정, 둘째=특이사항.</summary>
    private static (string name, string process, string note) ParseEquipName(string full)
    {
        full = (full ?? "").Trim();
        var open = full.IndexOf('(');
        if (open < 0) return (full, "", "");
        var name = full[..open].Trim();
        var groups = new List<string>();
        int i = open;
        while (i < full.Length)
        {
            var o = full.IndexOf('(', i);
            if (o < 0) break;
            var c = full.IndexOf(')', o + 1);
            if (c < 0) break;
            groups.Add(full[(o + 1)..c].Trim());
            i = c + 1;
        }
        return (name, groups.Count > 0 ? groups[0] : "", groups.Count > 1 ? groups[1] : "");
    }

    private static void SeedInventory(CleanPotalDbContext db)
    {
        if (db.InventoryItems.Any()) return;
        // (구역, 품목명, 현재재고, 안전재고, 최소발주, 발주회사)
        var seeds = new (string Loc, string Name, string Cur, string Apt, string Min, string Sup)[]
        {
            ("메탈 반입구", "검정색 토너", "", "3EA", "4EA", "신도비엠"),
            ("메탈 반입구", "빨간색 토너", "", "3EA", "4EA", "신도비엠"),
            ("메탈 반입구", "노란색 토너", "", "3EA", "4EA", "신도비엠"),
            ("메탈 반입구", "파란색 토너", "", "3EA", "4EA", "신도비엠"),
            ("메탈 반입구", "에어캡", "6", "8EA", "10EA", "주식회사 우암"),
            ("메탈 반입구", "투명테이프", "50EA 이상", "50EA", "100EA", "인터넷"),
            ("메탈 반입구", "비닐장갑", "3", "2팩", "1팩", "인터넷"),
            ("메탈 반입구", "방진용 덧신", "50팩 이상", "50팩", "200팩", "세이프티존"),
            ("메탈 반입구", "멤브레인용 보안 라벨", "50매 이상", "50매", "200매", "다인정보기술"),
            ("논메탈 반입구", "크린페이퍼", "28", "10EA", "40EA", "KM"),
            ("논메탈 반입구", "손타라(롤)와이퍼", "5", "6EA", "8EA", "KM"),
            ("논메탈 반입구", "부직포 와이퍼", "19", "2EA", "10EA", "KM"),
            ("논메탈 반입구", "극세사 와이퍼", "20", "10팩", "10팩", "KM"),
            ("논메탈 반입구", "에탄올 와이퍼", "16", "5팩", "10팩", "KM"),
            ("논메탈 반입구", "스티키매트", "5", "5EA", "10EA", "KM"),
            ("논메탈 반입구", "무접지 테이프(TTS용)", "", "2EA", "2EA", "인터넷"),
            ("논메탈 반입구", "방진테이프(제품포장용)", "30EA 이상", "30EA", "300EA", "코어텍"),
            ("논메탈 반입구", "흰라벨(10*9)", "48", "6롤", "20롤", "이엔시스"),
            ("논메탈 반입구", "빨간라벨(10*9)", "13", "4롤", "20롤", "이엔시스"),
            ("논메탈 반입구", "노란라벨(10*9)", "9", "4롤", "20롤", "이엔시스"),
            ("논메탈 반입구", "초록라벨(10*9)", "20", "4롤", "20롤", "이엔시스"),
            ("논메탈 반입구", "영신 A급 라벨 小 중국", "600매 이상", "600매", "1,200매", "다인정보기술"),
            ("논메탈 반입구", "영신 A급 라벨 小 한국", "600매 이상", "600매", "1,200매", "다인정보기술"),
            ("논메탈 반입구", "영신 A급 라벨 大 중국", "150매 이상", "150매", "300매", "다인정보기술"),
            ("논메탈 반입구", "영신 A급 라벨 大 한국", "150매 이상", "150매", "300매", "다인정보기술"),
            ("논메탈 반입구", "GP 스티커", "30매 이상", "30매", "20매", "영신쿼츠, 금강쿼츠"),
            ("논메탈 반입구", "손바닥 스티커", "2", "1롤", "2롤", "영신쿼츠, 금강쿼츠"),
            ("논메탈 반입구", "리테이너링 포장 박스", "400", "40SET", "500SET", "주안포장"),
            ("OFFICE 보관", "내산 방진복", "6", "10EA", "5EA", "수성안전"),
            ("OFFICE 보관", "내산 앞치마", "23", "5EA", "5EA", "수성안전"),
            ("OFFICE 보관", "심리스 글러브", "10", "5팩", "10팩", "KM"),
            ("세정랩", "MSDS 안전 스티커(SD-1)", "", "50매", "100매", "디자인톡"),
            ("세정랩", "SD-1 말통", "", "50EA", "100EA", "화진"),
            ("세정랩", "SD-1 속마개", "", "50EA", "100EA", "화진"),
        };
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        for (int i = 0; i < seeds.Length; i++)
        {
            var s = seeds[i];
            db.InventoryItems.Add(new InventoryItem
            {
                OrderNo = i + 1, StorageLocation = s.Loc, ItemName = s.Name,
                CurrentStock = s.Cur, AppropriateStock = s.Apt, MinOrderQty = s.Min, Supplier = s.Sup,
                RegisteredDate = today,
            });
        }
        db.SaveChanges();
        Console.WriteLine($"[seed] 현장 재고 {seeds.Length}품목 시드");
    }

    private static void SeedInspection(CleanPotalDbContext db)
    {
        if (db.InspectionItems.Any()) return;
        string[] zones = { "metal_in", "metal_out", "nonmetal_in", "nonmetal_out" };
        string[] items =
        {
            "작업장 정리정돈 상태 확인",
            "바닥 청결 및 누수 여부 확인",
            "측정 장비 정상 작동 확인",
            "약품 보관 상태 및 라벨 확인",
            "보호구 착용 및 비치 확인",
            "소화기·안전 설비 점검",
            "작업 일지 기록 확인",
        };
        foreach (var z in zones)
            for (int i = 0; i < items.Length; i++)
                db.InspectionItems.Add(new InspectionItem { Zone = z, SortOrder = i + 1, Text = items[i] });
        db.SaveChanges();
    }
}
