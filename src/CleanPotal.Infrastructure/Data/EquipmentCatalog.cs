using System.Text.RegularExpressions;
using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 설비 목록 하나로 — 스케줄 보드 설비(ScheduleEquipments)가 모든 화면의 설비 목록이다(2026-09-30).
///
/// 예전에는 스케줄 보드·업무 기록(약액·BAKE·KOH)·ICP-MS 가 설비 목록을 따로 들고 있어 이름이 어긋났다
/// (보드 MSC01-1 / 업무 기록 MSC02-1, NDC08 은 업무 기록에만, BAKE 오븐은 보드에 없음).
/// 이제 보드 설비 표에 라인·종류·보드 표시 여부를 두고, 보드에 줄로 안 나오는 BAKE·DRY 오븐도 여기에 넣는다.
/// 설비 이름·공정을 고치는 곳은 스케줄 보드의 '설비 &amp; 레시피 관리' 한 곳이다.
/// </summary>
public static partial class EquipmentCatalog
{
    /// <summary>기본 설비 목록 — 보드 순서대로. Show=false 는 보드에 안 보이는 설비(목록에만).</summary>
    public static readonly (string Name, string Group, string Process, string Line, string Kind, bool Show)[] Defaults =
    {
        ("MDC01", "MDC", "POLY", "METAL", EquipKinds.Clean, true),
        ("MDC02", "MDC", "Hot Chemical", "METAL", EquipKinds.Clean, true),
        ("MDC03", "MDC", "Hot Chemical", "METAL", EquipKinds.Clean, true),
        ("MDC04", "MDC", "POLY", "METAL", EquipKinds.Clean, true),
        ("MDC05", "MDC", "TEOS", "METAL", EquipKinds.Clean, true),
        ("MDC06", "MDC", "ALO/HFO", "METAL", EquipKinds.Clean, true),
        ("MDC07", "MDC", "POLY", "METAL", EquipKinds.Clean, true),
        ("MDC08", "MDC", "N,G,D-POLY", "METAL", EquipKinds.Clean, true),
        ("MDC09", "MDC", "SIGE", "METAL", EquipKinds.Clean, true),
        ("MDC10", "MDC", "ALO/HFO", "METAL", EquipKinds.Clean, true),
        ("MSC02-1", "MSC", "POLY/대대배치", "METAL", EquipKinds.Clean, true),
        ("MSC02-2", "MSC", "Rinse 전용", "METAL", EquipKinds.Clean, true),
        ("NDC01", "NDC", "WOOAM", "N-METAL", EquipKinds.Clean, true),
        ("NDC02", "NDC", "OXIDE", "N-METAL", EquipKinds.Clean, true),
        ("NDC03", "NDC", "A급", "N-METAL", EquipKinds.Clean, true),
        ("NDC04", "NDC", "A급", "N-METAL", EquipKinds.Clean, true),
        ("NDC05", "NDC", "N,G,D-POLY", "N-METAL", EquipKinds.Clean, true),
        ("NDC06", "NDC", "Hot Chemical", "N-METAL", EquipKinds.Clean, true),
        ("NDC07", "NDC", "SiN", "N-METAL", EquipKinds.Clean, true),
        ("NDC08", "NDC", "OTT", "N-METAL", EquipKinds.Clean, true),
        ("SPC01", "SPC", "", "N-METAL", EquipKinds.Clean, true),
        ("SPC02", "SPC", "", "N-METAL", EquipKinds.Clean, true),
        ("RFC01", "RFC", "", "METAL", EquipKinds.Clean, true),
        // ── 보드에 안 보이는 설비 ──
        ("NSC01", "NSC", "", "N-METAL", EquipKinds.Clean, false),
        ("MBO01-1", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO01-2", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO02-1", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO02-2", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO03-1", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO03-2", "BAKE", "BAKE", "METAL", EquipKinds.Bake, false),
        ("MBO04-1", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("MBO04-2", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO01-1", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO01-2", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO02-1", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO02-2", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO03-1", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("NBO03-2", "BAKE", "BAKE", "N-METAL", EquipKinds.Bake, false),
        ("MDO01", "DRY", "", "METAL", EquipKinds.Dry, false),
        ("MQO01", "DRY", "QUARTZ", "METAL", EquipKinds.Dry, false),
        ("NDO01", "DRY", "", "N-METAL", EquipKinds.Dry, false),
    };

    [GeneratedRegex("^[A-Z]BO\\d", RegexOptions.CultureInvariant)]
    private static partial Regex BakePattern();
    [GeneratedRegex("^[A-Z][DQ]O\\d", RegexOptions.CultureInvariant)]
    private static partial Regex DryPattern();

    /// <summary>코드로 종류를 짐작한다 — xBOnn 은 BAKE 오븐, xDOnn·xQOnn 은 DRY 오븐, 그 밖은 세정 설비.</summary>
    public static string GuessKind(string code) =>
        BakePattern().IsMatch(code) ? EquipKinds.Bake : DryPattern().IsMatch(code) ? EquipKinds.Dry : EquipKinds.Clean;

    /// <summary>코드로 라인을 짐작한다 — 기본 목록에 있으면 그 값, 아니면 N·SPC 로 시작하면 N-METAL.</summary>
    public static string GuessLine(string code)
    {
        foreach (var d in Defaults) if (d.Name == code) return d.Line;
        return code.StartsWith('N') || code.StartsWith("SPC", StringComparison.Ordinal) ? "N-METAL" : "METAL";
    }

    /// <summary>코드로 보드 묶음을 짐작한다 — 오븐은 BAKE/DRY, 그 밖은 앞 영문(MDC·NDC·SPC …).</summary>
    public static string GuessGroup(string code)
    {
        var kind = GuessKind(code);
        if (kind != EquipKinds.Clean) return kind;
        var m = Regex.Match(code, "^[A-Z]+");
        return m.Success ? m.Value : "기타";
    }

    /// <summary>
    /// 설비 코드 정리 — 대문자·공백 제거, 숫자 앞의 0 은 맞춘다(MSC1-1 과 MSC01-1 을 같은 설비로).
    /// 엑셀마다 같은 설비를 조금씩 다르게 적어 두었다(MSC1-1 / MSC01-1, "MDC 01").
    /// </summary>
    public static string NormalizeCode(string? raw)
    {
        var s = Regex.Replace((raw ?? "").Trim().ToUpperInvariant(), @"\s+", "");
        return Regex.Replace(s, @"^([A-Z]+)0*(\d+)", m => m.Groups[1].Value + m.Groups[2].Value.PadLeft(2, '0'));
    }

    /// <summary>표에 없는 기본 설비를 뒤에 붙인다(저장은 부른 쪽이). 붙인 이름을 돌려준다.</summary>
    public static List<string> AddMissingDefaults(CleanPotalDbContext db, List<ScheduleEquipment> all)
    {
        var added = new List<string>();
        var slot = all.Count == 0 ? -1 : all.Max(e => e.Slot);
        var order = all.Count == 0 ? -1 : all.Max(e => e.OrderIndex);
        foreach (var d in Defaults)
        {
            if (all.Any(e => e.Name == d.Name)) continue;
            var e = new ScheduleEquipment
            {
                Name = d.Name, GroupName = d.Group, Process = d.Process, Line = d.Line, Kind = d.Kind,
                ShowOnBoard = d.Show, Slot = ++slot, OrderIndex = ++order, IsActive = true,
            };
            db.ScheduleEquipments.Add(e);
            all.Add(e);
            added.Add(d.Name);
        }
        return added;
    }

    /// <summary>표가 비어 있으면 기본 설비로 채운다(새 DB).</summary>
    public static void SeedDefaults(CleanPotalDbContext db)
    {
        if (db.ScheduleEquipments.Any()) return;
        var added = AddMissingDefaults(db, new List<ScheduleEquipment>());
        db.SaveChanges();
        Console.WriteLine($"[seed] 설비 {added.Count}대 시드(스케줄 보드 {Defaults.Count(d => d.Show)}대 + 목록 전용)");
    }

    /// <summary>
    /// 기존 DB 를 통합 설비 목록으로 한 번 옮긴다 — 라인·종류가 하나도 채워지지 않은 DB 에서만 돈다.
    /// ① 보드의 옛 이름 MSC01-1/-2 → MSC02-1/-2 (현장·약액 기록·ICP-MS 가 모두 MSC02 — 2026-09-30 확인)
    /// ② 라인·종류 채우기(업무 기록 설비 목록에 같은 코드가 있으면 그 라인)
    /// ③ 업무 기록 설비 목록에만 있던 설비(BAKE 오븐 등)를 보드에 안 보이는 설비로 옮김
    /// ④ 새 기본 설비(NDC08·SPC01·SPC02·RFC01 보드 표시, DRY 오븐·NSC01 목록 전용) 추가
    /// </summary>
    public static void Backfill(CleanPotalDbContext db)
    {
        try
        {
            BackfillCore(db);
        }
        catch (Exception ex)
        {
            // 옮기지 못해도 보드·기록은 그대로 동작한다(종류 빈 칸은 코드로 짐작). 다음 기동 때 다시 한다.
            Console.WriteLine($"[equip][경고] 설비 목록 통합 실패(다음 기동 때 다시 시도): {ex.Message}");
        }
    }

    private static void BackfillCore(CleanPotalDbContext db)
    {
        var all = db.ScheduleEquipments.ToList();
        if (all.Count == 0 || all.Any(e => e.Kind != "")) return;

        // ① MSC 이름
        var renamed = 0;
        foreach (var n in new[] { "1", "2" })
        {
            var old = all.FirstOrDefault(e => e.Name == $"MSC01-{n}");
            if (old is not null && all.All(e => e.Name != $"MSC02-{n}")) { old.Name = $"MSC02-{n}"; renamed++; }
        }

        // ② 라인·종류
        List<WorkEquipment> work;
        try { work = db.WorkEquipments.AsNoTracking().ToList(); }
        catch { work = new(); }   // 업무 기록 설비 표가 없는 DB
        foreach (var e in all)
        {
            var w = work.FirstOrDefault(x => x.Code == e.Name);
            e.Line = w?.Line is { Length: > 0 } l ? l : GuessLine(e.Name);
            e.Kind = GuessKind(e.Name);
        }

        // ③ 업무 기록 설비 목록에만 있던 설비
        var slot = all.Max(e => e.Slot);
        var order = all.Max(e => e.OrderIndex);
        var moved = new List<string>();
        foreach (var w in work.OrderBy(x => x.SortOrder))
        {
            if (all.Any(e => e.Name == w.Code)) continue;
            var d = Defaults.FirstOrDefault(x => x.Name == w.Code);
            var e = new ScheduleEquipment
            {
                Name = w.Code,
                GroupName = d.Name is null ? GuessGroup(w.Code) : d.Group,
                Process = d.Name is null ? w.Process : d.Process,
                Line = w.Line is { Length: > 0 } l ? l : GuessLine(w.Code),
                Kind = GuessKind(w.Code),
                ShowOnBoard = d.Name is not null && d.Show,
                Slot = ++slot, OrderIndex = ++order, IsActive = w.IsActive,
            };
            db.ScheduleEquipments.Add(e);
            all.Add(e);
            moved.Add(w.Code);
        }

        // ④ 새 기본 설비
        var added = AddMissingDefaults(db, all);

        // 보드 묶음 표에 없는 묶음 이름을 붙인다(표가 비어 있으면 보드가 처음 열 때 채운다)
        var groups = db.ScheduleEquipGroups.ToList();
        if (groups.Count > 0)
        {
            var gOrder = groups.Max(g => g.OrderIndex);
            foreach (var name in all.Where(e => e.IsActive).Select(e => e.GroupName).Distinct())
                if (name.Length > 0 && groups.All(g => g.Name != name))
                {
                    var g = new ScheduleEquipGroup { Name = name, OrderIndex = ++gOrder };
                    db.ScheduleEquipGroups.Add(g);
                    groups.Add(g);
                }
        }

        db.SaveChanges();
        Console.WriteLine($"[equip] 설비 목록 통합: 이름 변경 {renamed}(MSC01→MSC02), 업무 기록 설비 옮김 {moved.Count}" +
                          $"({string.Join(",", moved)}), 새 설비 {added.Count}({string.Join(",", added)})");
    }
}
