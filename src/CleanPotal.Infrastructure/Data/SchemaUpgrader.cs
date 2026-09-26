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
        ("BrokenRecords",      "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("Dispatches",         "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("MaterialDayNotes",   "RowVersion", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("TeamEvents",         "CreatorUserId", "int NULL", "INTEGER NULL"),
        // 교대 근무 조 — 팀 이름을 바꿔도 근무 예측이 따라오게 하는 값
        // 직급(호칭). 직위(JobTitle)와 별개 — 기존 값은 건드리지 않고 빈 칸으로 추가된다.
        ("Users",              "Rank",       "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // MES 권한. 기본값 1(조회) 이라 기존 사용자도 컬럼이 생기는 순간 바로 MES 를 볼 수 있다
        // — 전 직원이 쓰는 시스템이라 관리자가 한 명씩 열어 줄 때까지 잠겨 있으면 안 된다.
        ("Users",              "AccessMes",  "int NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        // MES 세부 권한(업체·제품·공정 마스터 수정, 공정 무효화 …). 기본은 빈 칸 — 등급과 달리 이쪽은
        // 관리자가 사람을 골라 켜 주는 권한이라, 컬럼이 생겼다고 아무에게나 열리면 안 된다.
        ("Users",              "MesPermissions", "nvarchar(200) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // 업체 관리 화면을 하나로 합치면서 — 같은 업체의 MES 쪽 자료를 잇는다. 없으면 null.
        ("Vendors",            "MesCustomerId", "int NULL", "INTEGER NULL"),
        ("OrgUnits",           "ShiftGroup", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        // 생산팀 여부(교대조와 별개 축). 기존 교대 팀은 ShiftGroup 으로 판정되므로 기본값 0 이어도 안전하다.
        ("OrgUnits",           "IsProduction", "bit NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        // WPF 시절 팀 이름 — 병행 기간에 임포트 값을 현재 이름으로 바꾸는 데 쓴다
        ("OrgUnits",           "LegacyNames", "nvarchar(400) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        // 달력 부서 표시 — 색·약칭·사용 여부
        ("OrgUnits",           "Color",      "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        ("OrgUnits",           "ShortName",  "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
        ("OrgUnits",           "IsActive",   "bit NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        // 대시보드 근무 현황·일정 달력에 띄울지. 기본 1 — 칸이 생겼다고 기존 화면에서 사라지면 안 된다.
        ("OrgUnits",           "ShowOnDashboard", "bit NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        ("OrgUnits",           "ShowOnCalendar",  "bit NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        // 온·습도 주기 기록 — 실제 수신과 구분한다(통신 끊김 판정의 근거가 흐려지면 안 된다)
        ("ZigbeeReadings",     "IsSnapshot", "bit NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
        ("ZigbeeThresholds",   "SnapshotIntervalMinutes", "int NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
        // 첨부가 어느 영역 화면의 것인지 — 받을 때 그 영역 조회 권한을 본다. 기존 첨부는 빈 칸(로그인만 확인).
        ("Attachments",        "Scope", "nvarchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''"),
    };

    /// <summary>(표, 인덱스 이름, 컬럼 목록) — 운영 DB 에 없으면 만든다. 이름은 EF 가 새 DB 에 만드는 이름과 같게 둔다.</summary>
    private static readonly (string Table, string Name, string[] Columns)[] Indexes =
    {
        ("ZigbeeReadings", "IX_ZigbeeReadings_IsSnapshot_ReceivedAt", new[] { "IsSnapshot", "ReceivedAt" }),
        ("ShiftSchedules", "IX_ShiftSchedules_TargetDate", new[] { "TargetDate" }),
    };

    private const string ZigbeeSensorSqlServer = """
        CREATE TABLE [ZigbeeSensors] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [DeviceId] nvarchar(100) NOT NULL,
            [Site] nvarchar(50) NOT NULL DEFAULT '',
            [DisplayName] nvarchar(100) NOT NULL DEFAULT '',
            [IsEnabled] bit NOT NULL DEFAULT 1,
            [SortOrder] int NOT NULL DEFAULT 0,
            [CreatedAt] datetime2 NOT NULL,
            [UpdatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ZigbeeSensors] PRIMARY KEY ([Id])
        )
        """;

    private const string ZigbeeSensorSqlite = """
        CREATE TABLE "ZigbeeSensors" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ZigbeeSensors" PRIMARY KEY AUTOINCREMENT,
            "DeviceId" TEXT NOT NULL,
            "Site" TEXT NOT NULL DEFAULT '',
            "DisplayName" TEXT NOT NULL DEFAULT '',
            "IsEnabled" INTEGER NOT NULL DEFAULT 1,
            "SortOrder" INTEGER NOT NULL DEFAULT 0,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL
        )
        """;

    private const string ZigbeeReadingSqlServer = """
        CREATE TABLE [ZigbeeReadings] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [DeviceId] nvarchar(100) NOT NULL,
            [Temperature] float NULL,
            [Humidity] float NULL,
            [Battery] int NULL,
            [LinkQuality] int NULL,
            [ReceivedAt] datetime2 NOT NULL,
            [IsSnapshot] bit NOT NULL DEFAULT 0,
            CONSTRAINT [PK_ZigbeeReadings] PRIMARY KEY ([Id])
        )
        """;

    private const string ZigbeeReadingSqlite = """
        CREATE TABLE "ZigbeeReadings" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ZigbeeReadings" PRIMARY KEY AUTOINCREMENT,
            "DeviceId" TEXT NOT NULL,
            "Temperature" REAL NULL,
            "Humidity" REAL NULL,
            "Battery" INTEGER NULL,
            "LinkQuality" INTEGER NULL,
            "ReceivedAt" TEXT NOT NULL,
            "IsSnapshot" INTEGER NOT NULL DEFAULT 0
        )
        """;

    private const string AttachmentSqlServer = """
        CREATE TABLE [Attachments] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [StoredName] nvarchar(80) NOT NULL,
            [Folder] nvarchar(20) NOT NULL,
            [FileName] nvarchar(260) NOT NULL,
            [ContentType] nvarchar(150) NOT NULL,
            [Size] bigint NOT NULL,
            [Kind] nvarchar(10) NOT NULL,
            [CreatedAt] datetime2 NOT NULL,
            [CreatedBy] nvarchar(100) NOT NULL,
            CONSTRAINT [PK_Attachments] PRIMARY KEY ([Id])
        )
        """;

    private const string AttachmentSqlite = """
        CREATE TABLE "Attachments" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY AUTOINCREMENT,
            "StoredName" TEXT NOT NULL,
            "Folder" TEXT NOT NULL,
            "FileName" TEXT NOT NULL,
            "ContentType" TEXT NOT NULL,
            "Size" INTEGER NOT NULL,
            "Kind" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "CreatedBy" TEXT NOT NULL
        )
        """;

    private const string BrokenOptionSqlServer = """
        CREATE TABLE [BrokenOptions] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [Kind] nvarchar(30) NOT NULL,
            [Name] nvarchar(60) NOT NULL,
            [OrderIndex] int NOT NULL DEFAULT 0,
            CONSTRAINT [PK_BrokenOptions] PRIMARY KEY ([Id])
        )
        """;

    private const string BrokenOptionSqlite = """
        CREATE TABLE "BrokenOptions" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_BrokenOptions" PRIMARY KEY AUTOINCREMENT,
            "Kind" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "OrderIndex" INTEGER NOT NULL DEFAULT 0
        )
        """;

    private const string HolidayOverrideSqlServer = """
        CREATE TABLE [HolidayOverrides] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [Date] date NOT NULL,
            [Name] nvarchar(40) NOT NULL,
            [IsOff] bit NOT NULL,
            [UpdatedAt] datetime2 NOT NULL,
            [UpdatedBy] nvarchar(100) NOT NULL,
            CONSTRAINT [PK_HolidayOverrides] PRIMARY KEY ([Id])
        )
        """;

    private const string HolidayOverrideSqlite = """
        CREATE TABLE "HolidayOverrides" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_HolidayOverrides" PRIMARY KEY AUTOINCREMENT,
            "Date" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "IsOff" INTEGER NOT NULL,
            "UpdatedAt" TEXT NOT NULL,
            "UpdatedBy" TEXT NOT NULL
        )
        """;

    private const string ScheduleEquipGroupSqlServer = """
        CREATE TABLE [ScheduleEquipGroups] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [Name] nvarchar(40) NOT NULL,
            [OrderIndex] int NOT NULL DEFAULT 0,
            CONSTRAINT [PK_ScheduleEquipGroups] PRIMARY KEY ([Id])
        )
        """;

    private const string ScheduleEquipGroupSqlite = """
        CREATE TABLE "ScheduleEquipGroups" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ScheduleEquipGroups" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL,
            "OrderIndex" INTEGER NOT NULL DEFAULT 0
        )
        """;

    private const string ZigbeeThresholdSqlServer = """
        CREATE TABLE [ZigbeeThresholds] (
            [Id] int IDENTITY(1,1) NOT NULL,
            [Scope] nvarchar(20) NOT NULL DEFAULT 'global',
            [ScopeKey] nvarchar(100) NOT NULL DEFAULT '',
            [TempNormalMin] float NOT NULL DEFAULT 0,
            [TempNormalMax] float NOT NULL DEFAULT 0,
            [TempWarnMin] float NOT NULL DEFAULT 0,
            [TempWarnMax] float NOT NULL DEFAULT 0,
            [HumidNormalMin] float NOT NULL DEFAULT 0,
            [HumidNormalMax] float NOT NULL DEFAULT 0,
            [HumidWarnMin] float NOT NULL DEFAULT 0,
            [HumidWarnMax] float NOT NULL DEFAULT 0,
            [OfflineAfterMinutes] int NOT NULL DEFAULT 5,
            [LowBatteryPercent] int NOT NULL DEFAULT 20,
            [UpdatedAt] datetime2 NOT NULL,
            [SnapshotIntervalMinutes] int NOT NULL DEFAULT 1,
            [UpdatedBy] nvarchar(100) NOT NULL DEFAULT '',
            CONSTRAINT [PK_ZigbeeThresholds] PRIMARY KEY ([Id])
        )
        """;

    private const string ZigbeeThresholdSqlite = """
        CREATE TABLE "ZigbeeThresholds" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ZigbeeThresholds" PRIMARY KEY AUTOINCREMENT,
            "Scope" TEXT NOT NULL DEFAULT 'global',
            "ScopeKey" TEXT NOT NULL DEFAULT '',
            "TempNormalMin" REAL NOT NULL DEFAULT 0,
            "TempNormalMax" REAL NOT NULL DEFAULT 0,
            "TempWarnMin" REAL NOT NULL DEFAULT 0,
            "TempWarnMax" REAL NOT NULL DEFAULT 0,
            "HumidNormalMin" REAL NOT NULL DEFAULT 0,
            "HumidNormalMax" REAL NOT NULL DEFAULT 0,
            "HumidWarnMin" REAL NOT NULL DEFAULT 0,
            "HumidWarnMax" REAL NOT NULL DEFAULT 0,
            "OfflineAfterMinutes" INTEGER NOT NULL DEFAULT 5,
            "LowBatteryPercent" INTEGER NOT NULL DEFAULT 20,
            "UpdatedAt" TEXT NOT NULL,
            "SnapshotIntervalMinutes" INTEGER NOT NULL DEFAULT 1,
            "UpdatedBy" TEXT NOT NULL DEFAULT ''
        )
        """;

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
            // 표와 그 인덱스를 한 트랜잭션으로 만든다 — 인덱스에서 실패하면 표도 되돌려 다음 실행 때 다시 시도한다.
            // (예전에는 표만 남고, 다음 실행은 "표가 있다" 고 보고 인덱스를 영영 만들지 않았다)
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? ContentAuditSqlite : ContentAuditSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE INDEX ""IX_ContentAudits_EntityType_EntityId"" ON ""ContentAudits"" (""EntityType"", ""EntityId"")"
                    : "CREATE INDEX [IX_ContentAudits_EntityType_EntityId] ON [ContentAudits] ([EntityType], [EntityId])");
                Exec(db, useSqlite
                    ? @"CREATE INDEX ""IX_ContentAudits_CreatedAt"" ON ""ContentAudits"" (""CreatedAt"")"
                    : "CREATE INDEX [IX_ContentAudits_CreatedAt] ON [ContentAudits] ([CreatedAt])");
            });
            Console.WriteLine("[schema] ContentAudits 테이블 생성(자료 변경 이력)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "Attachments"))
        {
            Exec(db, useSqlite ? AttachmentSqlite : AttachmentSqlServer);
            Console.WriteLine("[schema] Attachments 테이블 생성(첨부 파일 보관소)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "BrokenOptions"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? BrokenOptionSqlite : BrokenOptionSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE UNIQUE INDEX ""IX_BrokenOptions_Kind_Name"" ON ""BrokenOptions"" (""Kind"", ""Name"")"
                    : "CREATE UNIQUE INDEX [IX_BrokenOptions_Kind_Name] ON [BrokenOptions] ([Kind], [Name])");
            });
            Console.WriteLine("[schema] BrokenOptions 테이블 생성(BROKEN 등록 드롭다운 목록)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "HolidayOverrides"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? HolidayOverrideSqlite : HolidayOverrideSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE UNIQUE INDEX ""IX_HolidayOverrides_Date"" ON ""HolidayOverrides"" (""Date"")"
                    : "CREATE UNIQUE INDEX [IX_HolidayOverrides_Date] ON [HolidayOverrides] ([Date])");
            });
            Console.WriteLine("[schema] HolidayOverrides 테이블 생성(관리자가 고친 공휴일)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "ScheduleEquipGroups"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? ScheduleEquipGroupSqlite : ScheduleEquipGroupSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE UNIQUE INDEX ""IX_ScheduleEquipGroups_Name"" ON ""ScheduleEquipGroups"" (""Name"")"
                    : "CREATE UNIQUE INDEX [IX_ScheduleEquipGroups_Name] ON [ScheduleEquipGroups] ([Name])");
            });
            Console.WriteLine("[schema] ScheduleEquipGroups 테이블 생성(스케줄보드 설비 묶음)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "ZigbeeThresholds"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? ZigbeeThresholdSqlite : ZigbeeThresholdSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE UNIQUE INDEX ""IX_ZigbeeThresholds_Scope_ScopeKey"" ON ""ZigbeeThresholds"" (""Scope"", ""ScopeKey"")"
                    : "CREATE UNIQUE INDEX [IX_ZigbeeThresholds_Scope_ScopeKey] ON [ZigbeeThresholds] ([Scope], [ScopeKey])");
            });
            Console.WriteLine("[schema] ZigbeeThresholds 테이블 생성(온·습도 판정 기준)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "ZigbeeSensors"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? ZigbeeSensorSqlite : ZigbeeSensorSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE UNIQUE INDEX ""IX_ZigbeeSensors_DeviceId"" ON ""ZigbeeSensors"" (""DeviceId"")"
                    : "CREATE UNIQUE INDEX [IX_ZigbeeSensors_DeviceId] ON [ZigbeeSensors] ([DeviceId])");
            });
            Console.WriteLine("[schema] ZigbeeSensors 테이블 생성(온·습도 센서 마스터)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "ZigbeeReadings"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? ZigbeeReadingSqlite : ZigbeeReadingSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE INDEX ""IX_ZigbeeReadings_DeviceId_ReceivedAt"" ON ""ZigbeeReadings"" (""DeviceId"", ""ReceivedAt"")"
                    : "CREATE INDEX [IX_ZigbeeReadings_DeviceId_ReceivedAt] ON [ZigbeeReadings] ([DeviceId], [ReceivedAt])");
            });
            Console.WriteLine("[schema] ZigbeeReadings 테이블 생성(온·습도 수신 이력)");
            applied++;
        }

        if (!TableExists(db, useSqlite, "TeamEventDepts"))
        {
            InTransaction(db, () =>
            {
                Exec(db, useSqlite ? TeamEventDeptSqlite : TeamEventDeptSqlServer);
                Exec(db, useSqlite
                    ? @"CREATE INDEX ""IX_TeamEventDepts_TeamEventId"" ON ""TeamEventDepts"" (""TeamEventId"")"
                    : "CREATE INDEX [IX_TeamEventDepts_TeamEventId] ON [TeamEventDepts] ([TeamEventId])");
                Exec(db, useSqlite
                    ? @"CREATE INDEX ""IX_TeamEventDepts_OrgUnitId"" ON ""TeamEventDepts"" (""OrgUnitId"")"
                    : "CREATE INDEX [IX_TeamEventDepts_OrgUnitId] ON [TeamEventDepts] ([OrgUnitId])");
            });
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

        foreach (var (table, name, columns) in Indexes)
        {
            if (!TableExists(db, useSqlite, table) || IndexExists(db, useSqlite, table, name)) continue;
            if (columns.Any(c => !ColumnExists(db, useSqlite, table, c))) continue;
            var cols = string.Join(", ", columns.Select(c => useSqlite ? $@"""{c}""" : $"[{c}]"));
            Exec(db, useSqlite
                ? $@"CREATE INDEX ""{name}"" ON ""{table}"" ({cols})"
                : $"CREATE INDEX [{name}] ON [{table}] ({cols})");
            Console.WriteLine($"[schema] {table} 인덱스 {name} 추가");
            applied++;
        }

        if (applied > 0)
            Console.WriteLine($"[schema] 추가 적용 {applied}건 완료 (기존 데이터는 변경하지 않음).");
    }

    private static bool TableExists(CleanPotalDbContext db, bool useSqlite, string table)
        => Scalar(db, useSqlite
            ? $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'"
            : $"SELECT COUNT(*) FROM sys.tables WHERE name = '{table}'") > 0;

    private static bool IndexExists(CleanPotalDbContext db, bool useSqlite, string table, string name)
        => Scalar(db, useSqlite
            ? $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{name}'"
            : $"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('{table}') AND name = '{name}'") > 0;

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

    private static void InTransaction(CleanPotalDbContext db, Action work)
    {
        // 바깥에서 이미 트랜잭션을 열었으면 그 안에서 한다.
        if (db.Database.CurrentTransaction is not null) { work(); return; }
        using var tx = db.Database.BeginTransaction();
        work();
        tx.Commit();
    }
}
