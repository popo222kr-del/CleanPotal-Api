using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;

namespace CleanPotal.Infrastructure.Data;

/// <summary>초기 시드 데이터 (마이그레이션 도입 전 개발용).</summary>
public static class DbSeeder
{
    public static void Seed(CleanPotalDbContext db)
    {
        SeedInspection(db);   // 점검 항목은 독립적으로 시드 (사용자 유무와 무관)
        if (db.Users.Any()) return;

        User U(string un, string name, string team, string job, bool admin = false) => new()
        {
            Username = un,
            PasswordHash = PasswordHasher.Hash("1234"),
            RealName = name,
            TeamName = team,
            JobTitle = job,
            EmployeeNumber = un,
            IsAdmin = admin,
            CanManageFiles = admin,
            CanManageNotices = admin,
            CanManageVendors = admin,
            CanManageSchedule = admin,
            CanAccessEtcMenu = admin,
        };

        db.Users.AddRange(
            U("1004", "박주언", "Office", "대리", admin: true),
            U("kim01", "김철수", "김팀", "조장"),
            U("kim02", "김영희", "김팀", "사원"),
            U("kim03", "김민수", "김팀", "사원"),
            U("kim04", "김지훈", "김팀", "사원"),
            U("jang01", "장동건", "장팀", "조장"),
            U("jang02", "장미란", "장팀", "사원"),
            U("jang03", "장서연", "장팀", "사원"),
            U("jang04", "장현우", "장팀", "사원")
        );
        db.SaveChanges();
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
