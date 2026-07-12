using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;

namespace CleanPotal.Infrastructure.Data;

/// <summary>초기 시드 데이터 (마이그레이션 도입 전 개발용).</summary>
public static class DbSeeder
{
    public static void Seed(CleanPotalDbContext db)
    {
        SeedInspection(db);       // 점검 항목 (실제 연동 전 기본값)
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
