using System.Text.Json;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 지금 보고 있는 포털이 어느 서버(개발·테스트·운영)이고 어느 빌드인지. 화면 왼쪽 위 배지·브라우저 탭 제목과
/// tools\servers.ps1 의 "상태 보기"가 쓴다 — 세 서버가 같은 화면이라 어디에 접속했는지 헷갈리지 않게.
///
///   Portal:EnvName    dev | test | prod (없으면 개발 모드면 dev, 아니면 prod). 테스트 서버는 deploy-test.ps1 이 test 로 띄운다.
///   build-info.json   앱 폴더에 있으면 커밋·빌드 시각을 읽는다(deploy-test.ps1 이 publish 할 때 만든다 —
///                     운영에는 같은 publish 폴더를 복사하므로 운영도 같은 값을 보인다).
///
/// 로그인 전(로그인 화면)에도 보여야 해서 익명으로 연다. 비밀값·경로는 담지 않는다.
/// </summary>
public sealed record PortalAbout(string Env, string EnvLabel, string Commit, string Subject, string BuiltAt, bool Dirty)
{
    public static PortalAbout Load(string? envName, bool isDevelopment, string contentRoot)
    {
        var env = (envName ?? "").Trim().ToLowerInvariant();
        if (env.Length == 0) env = isDevelopment ? "dev" : "prod";
        var label = env switch { "dev" => "개발", "test" => "테스트", "prod" => "운영", _ => env };

        string commit = "", subject = "", builtAt = "";
        var dirty = false;
        try
        {
            var path = Path.Combine(contentRoot, "build-info.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                commit = S("commit");
                subject = S("subject");
                builtAt = S("builtAt");
                dirty = r.TryGetProperty("dirty", out var d) && d.ValueKind == JsonValueKind.True;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[about] build-info.json 을 읽지 못했습니다: {ex.Message}");
        }
        return new PortalAbout(env, label, commit, subject, builtAt, dirty);
    }
}
