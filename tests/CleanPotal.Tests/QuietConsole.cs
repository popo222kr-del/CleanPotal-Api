using System.Runtime.CompilerServices;

namespace CleanPotal.Tests;

/// <summary>
/// 테스트 중에는 포털 기동 기록([schema]·[seed]·[mes]·[storage] …)을 화면에 내지 않는다.
///
/// 테스트가 포털을 띄울 때마다 기동 기록이 쏟아져 결과를 가리고, 윈도우 테스트 실행기에서는 한글이
/// 깨져(인코딩이 섞여) 알아볼 수도 없었다. 합격·불합격은 테스트 결과로 보므로 기동 기록은 필요 없다.
/// 원인을 볼 때는 환경변수 CLEANPOTAL_TEST_CONSOLE=1 을 주고 돌리면 예전처럼 보인다.
/// </summary>
internal static class QuietConsole
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (Environment.GetEnvironmentVariable("CLEANPOTAL_TEST_CONSOLE") == "1") return;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }
}
