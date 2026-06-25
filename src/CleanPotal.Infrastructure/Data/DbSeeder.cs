using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;

namespace CleanPotal.Infrastructure.Data;

/// <summary>초기 시드 데이터 (마이그레이션 도입 전 개발용).</summary>
public static class DbSeeder
{
    public static void Seed(CleanPotalDbContext db)
    {
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
}
