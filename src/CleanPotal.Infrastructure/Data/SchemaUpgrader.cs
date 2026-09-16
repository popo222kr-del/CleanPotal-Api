using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Data;

/// <summary>
/// 기존 DB에 <b>새로 생긴 컬럼·테이블만</b> 덧붙인다.
///
/// 왜 필요한가: SQL Server 경로는 <c>EnsureCreated()</c> 를 쓰는데, 이 메서드는
/// **테이블이 하나도 없을 때만** 스키마를 만든다. 이미 운영 중인 DB 에는
/// 모델에 새로 추가된 컬럼이 절대 반영되지 않아, 앱이 없는 컬럼을 조회하다 죽는다.
/// 스키마를 다시 만들려면 데이터를 지워야 하므로 쓸 수 없다.
///
/// 안전 원칙 — 이 클래스는 <b>추가만</b> 한다:
/// - 없는 컬럼 ADD, 없는 테이블 CREATE 만 수행한다.
/// - DROP / 타입 변경 / 데이터 이동은 하지 않는다. 기존 값은 건드리지 않는다.
/// - 이미 있으면 아무것도 하지 않는다(여러 번 실행해도 안전).
/// - 실행한 문장은 콘솔에 남긴다.
/// </summary>
public static class SchemaUpgrader
{
    /// <summary>(테이블, 컬럼, SQL Server 타입, SQLite 타입) — 추가 대상.</summary>
    private static readonly (string Table, string Column, string SqlServer, string Sqlite)[] Columns =
    {
        // 작성자를 이름 대신 계정 ID 로 식별하기 위한 컬럼
        ("Notices",            "CreatorUserId", "int NULL", "INTEGER NULL"),
        ("Handovers",          "CreatorUserId", "int NULL", "INTEGER NULL"),
        ("ProductionMeetings", "CreatorUserId", "int NULL", "INTEGER NULL"),
        ("ProdReqs",           "CreatorUserId", "int NULL", "INTEGER NULL"),
        ("Reports",            "CreatorUserId", "int NULL", "INTEGER NULL"),
        ("Reports",            "CreatorName",   "nvarchar(100) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // 동시 수정 감지용 버전 (기존 행은 0 으로 시작)
        ("Notices",            "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("Handovers",          "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("ProductionMeetings", "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("ProdReqs",           "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("Reports",            "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        // 교대 근무 조 — 팀 이름을 바꿔도 근무 예측이 따라오게 하는 값
        // 직급(호칭). 직위(JobTitle)와 별개 — 기존 값은 건드리지 않고 빈 칸으로 추가된다.
        ("Users",              "Rank",       "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // MES 권한. 기본값 1(조회) 이라 기존 사용자도 컬럼이 생기는 순간 바로 MES 를 볼 수 있다
        // — 전 직원이 쓰는 시스템이라 관리자가 한 명씩 열어 줄 때까지 잠겨 있으면 안 된다.
        ("Users",              "AccessMes",  "int NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        ("OrgUnits",           "ShiftGroup", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        // 생산팀 여부(교대조와 별개 축). 기존 교대 팀은 ShiftGroup 으로 판정되므로 기본값 0 이어도 안전하다.
        ("OrgUnits",           "IsProduction", "bit NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        // WPF 시절 팀 이름 — 병행 기간에 임포트 값을 현재 이름으로 바꾸는 데 쓴다
        ("OrgUnits",           "LegacyNames", "nvarchar(400) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // 달력 부서 표시 — 색·약칭·사용 여부
        ("OrgUnits",           "Color",      "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        ("OrgUnits",           "ShortName",  "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        ("OrgUnits",           "IsActive",   "bit NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
    };

    private const string TeamEventDeptSqlServer = """
        CREATE TABLE [TeamEventDepts] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [TeamEventId] int NOT NULL,
            [OrgUnitId] int NOT NULL,
            CONSTRAINT [PK_TeamEventDepts] PRIMARY KEY ([Id])
        )
        """;

    private const string TeamEventDeptSqlite = """
        CREATE TABLE "TeamEventDepts" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_TeamEventDepts" PRIMARY KEY AUTOINCREMENT,
            "TeamEventId" INTEGER NOT NULL,
            "OrgUnitId" INTEGER NOT NULL
        )
        """;

    private const string ContentAuditSqlServer = """
        CREATE TABLE [ContentAudits] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [EntityType] nvarchar(40) NOT NULL DEFAULT '',
            [EntityId] int NOT NULL DEFAULT 0,
            [Action] nvarchar(40) NOT NULL DEFAULT '',
            [Detail] nvarchar(max) NOT NULL DEFAULT '',
            [ByUserId] int NULL,
            [ByUserName] nvarchar(100) NOT NULL DEFAULT '',
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ContentAudits] PRIMARY KEY ([Id])
        )
        """;

    private const string ContentAuditSqlite = """
        CREATE TABLE "ContentAudits" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ContentAudits" PRIMARY KEY AUTOINCREMENT,
            "EntityType" TEXT NOT NULL DEFAULT '',
            "EntityId" INTEGER NOT NULL DEFAULT 0,
            "Action" TEXT NOT NULL DEFAULT '',
            "Detail" TEXT NOT NULL DEFAULT '',
            "ByUserId" INTEGER NULL,
            "ByUserName" TEXT NOT NULL DEFAULT '',
            "CreatedAt" TEXT NOT NULL
        )
        """;

    /// <summary>추가가 필요한 것만 적용한다. 아무것도 없으면 조용히 끝난다.</summary>
    public static void Run(CleanPotalDbContext db, bool useSqlite)
    {
        var applied = 0;

        if (!TableExists(db, useSqlite, "ContentAudits"))
        {
            Exec(db, useSqlite ? ContentAuditSqlite : ContentAuditSqlServer);
            Exec(db, useSqlite
                ? @"CREATE INDEX ""IX_ContentAudits_EntityType_EntityId"" ON ""ContentAudits"" (""EntityType"", ""EntityId"")"
                : "CREATE INDEX [IX_ContentAudits_EntityType_EntityId] ON [ContentAudits] ([EntityType], [EntityId])");
            Exec(db, useSqlite
                ? @"CREATE INDEX ""IX_ContentAudits_CreatedAt"" ON ""ContentAudits"" (""CreatedAt"")"
                : "CREATE INDEX [IX_ContentAudits_CreatedAt] ON [ContentAudits] ([CreatedAt])");
            Console.WriteLine("[schema] ContentAudits 테이블 생성(자료 변경 이력)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "TeamEventDepts"))
        {
            Exec(db, useSqlite ? TeamEventDeptSqlite : TeamEventDeptSqlServer);
            Exec(db, useSqlite
                ? @"CREATE INDEX ""IX_TeamEventDepts_TeamEventId"" ON ""TeamEventDepts"" (""TeamEventId"")"
                : "CREATE INDEX [IX_TeamEventDepts_TeamEventId] ON [TeamEventDepts] ([TeamEventId])");
            Exec(db, useSqlite
                ? @"CREATE INDEX ""IX_TeamEventDepts_OrgUnitId"" ON ""TeamEventDepts"" (""OrgUnitId"")"
                : "CREATE INDEX [IX_TeamEventDepts_OrgUnitId] ON [TeamEventDepts] ([OrgUnitId])");
            Console.WriteLine("[schema] TeamEventDepts 테이블 생성(일정↔부서 연결)");
            applied++;
        }

        foreach (var (table, column, sqlServerType, sqliteType) in Columns)
        {
            if (!TableExists(db, useSqlite, table)) continue;      // 아직 없는 테이블은 EnsureCreated 가 만든다
            if (ColumnExists(db, useSqlite, table, column)) continue;

            var type = useSqlite ? sqliteType : sqlServerType;
            Exec(db, useSqlite
                ? $@"ALTER TABLE ""{table}"" ADD COLUMN ""{column}"" {type}"
                : $"ALTER TABLE [{table}] ADD [{column}] {type}");
            Console.WriteLine($"[schema] {table}.{column} 컬럼 추가");
            applied++;
        }

        if (applied > 0)
            Console.WriteLine($"[schema] 추가 적용 {applied}건 완료 (기존 데이터는 변경하지 않음).");
    }

    private static bool TableExists(CleanPotalDbContext db, bool useSqlite, string table)
        => Scalar(db, useSqlite
            ? $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'"
            : $"SELECT COUNT(*) FROM sys.tables WHERE name = '{table}'") > 0;

    private static bool ColumnExists(CleanPotalDbContext db, bool useSqlite, string table, string column)
        => Scalar(db, useSqlite
            ? $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'"
            : $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = '{column}'") > 0;

    // 테이블/컬럼 이름은 위 배열의 고정 문자열이라 외부 입력이 섞이지 않는다.
    private static int Scalar(CleanPotalDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        finally { if (opened) conn.Close(); }
    }

    private static void Exec(CleanPotalDbContext db, string sql) => db.Database.ExecuteSqlRaw(sql);
}
