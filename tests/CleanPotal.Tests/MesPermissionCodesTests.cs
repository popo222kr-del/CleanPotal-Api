using CleanPotal.Core;
using ProductionManagement.Domain.Enums;

namespace CleanPotal.Tests;

/// <summary>
/// MES 세부 권한 코드는 포털(문자열)과 MES(enum) 두 곳에 적혀 있다. 포털 Core 는 MES 를 참조하지
/// 않아 컴파일러가 둘을 맞춰 주지 못하므로, 어긋나면 여기서 잡는다.
///
/// 어긋나면 조용히 권한이 사라진다 — 포털 화면에서 켠 항목이 MES 쪽 이름과 한 글자라도 다르면
/// 저장은 되지만 아무 권한도 붙지 않고, 사용자는 "켰는데 안 된다" 는 상태가 된다.
/// </summary>
public class MesPermissionCodesTests
{
    [Fact]
    public void 포털_코드_목록은_MES_권한_enum_과_같다()
    {
        var mes = Enum.GetNames<PermissionCode>().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var portal = MesPermissionCodes.All.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(mes, portal);
    }

    [Fact]
    public void 모르는_코드는_버린다()
    {
        var parsed = MesPermissionCodes.Parse("Rollback,NotARealCode,AdminProduct");
        Assert.Equal(new[] { MesPermissionCodes.AdminProduct, MesPermissionCodes.Rollback },
            parsed.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("rollback", "")]                       // 대소문자가 다르면 다른 코드다
    [InlineData("Rollback,Rollback", "Rollback")]      // 중복은 한 번만
    [InlineData(" Rollback , AdminProduct ", "Rollback,AdminProduct")]
    [InlineData("AdminProduct,Rollback", "Rollback,AdminProduct")]  // 순서는 목록 순서로 고정
    public void 저장_형식은_한_가지로_정리된다(string? raw, string expected)
    {
        Assert.Equal(expected, MesPermissionCodes.Normalize(raw));
    }

    [Fact]
    public void 정리한_문자열은_그대로_enum_으로_돌아온다()
    {
        foreach (var name in MesPermissionCodes.All)
        {
            Assert.True(Enum.TryParse<PermissionCode>(name, out _), $"{name} 은(는) MES 권한 코드가 아니다");
        }
    }
}
