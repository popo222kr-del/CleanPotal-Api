using CleanPotal.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// SQL Server 운영 DB 에 빠진 컬럼을 덧붙이는 문장. 기존 행이 있어도 실패하지 않아야 한다 —
/// NOT NULL 컬럼은 반드시 기본값 제약을 달고, 옛 datetime 은 1753년 이전 값을 못 담는다.
/// </summary>
public class SqlServerSchemaSqlTests
{
    [Fact]
    public void 널_허용_컬럼은_기본값_없이_추가한다()
        => Assert.Equal("ALTER TABLE [Attachments] ADD [Scope] nvarchar(max) NULL",
            DatabaseSchemaInitializer.SqlServerAddColumnSql("Attachments", "Scope", "nvarchar(max)", true, typeof(string)));

    [Theory]
    [InlineData(typeof(string), "nvarchar(max)", "N''")]
    [InlineData(typeof(int), "int", "0")]
    [InlineData(typeof(bool), "bit", "0")]
    [InlineData(typeof(DateTime), "datetime2", "'0001-01-01T00:00:00'")]
    [InlineData(typeof(DateTime), "datetime", "'1900-01-01T00:00:00'")]
    [InlineData(typeof(DateOnly), "date", "'0001-01-01'")]
    public void 필수_컬럼은_기본값_제약을_단다(Type clr, string store, string literal)
        => Assert.Equal($"ALTER TABLE [T] ADD [C] {store} NOT NULL CONSTRAINT [DF_T_C] DEFAULT {literal}",
            DatabaseSchemaInitializer.SqlServerAddColumnSql("T", "C", store, false, clr));

    [Fact]
    public void 이름의_닫는_괄호는_이스케이프한다()
        => Assert.StartsWith("ALTER TABLE [a]]b] ADD [c]]d]",
            DatabaseSchemaInitializer.SqlServerAddColumnSql("a]b", "c]d", "int", true, typeof(int)));
}
