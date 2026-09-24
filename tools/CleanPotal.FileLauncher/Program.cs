using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;

namespace CleanPotal.FileLauncher;

internal static class Program
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".csv",
        ".docx", ".pptx",
        ".pdf", ".txt", ".png", ".jpg", ".jpeg", ".tif", ".tiff"
    };

    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var settings = LoadSettings();
            ValidateServer(settings);

            if (args.Length == 1 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "CleanPotal 실행 도우미 설정과 보안 검사를 통과했습니다.",
                    "CleanPotal 실행 도우미",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (args.Length != 1)
                throw new InvalidOperationException("웹에서 전달된 실행 주소가 없습니다.");

            var ticket = ParseTicket(args[0]);
            using var client = new HttpClient
            {
                BaseAddress = new Uri(settings.ApiBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 64 * 1024
            };

            using var response = await client.PostAsync(
                $"/api/portal/launch-tickets/{ticket}/redeem",
                content: null);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ReadError(json) ?? $"서버가 실행을 거부했습니다 ({(int)response.StatusCode}).");

            var path = ReadPath(json);
            ValidateDocument(path, settings.AllowedRoots);

            var answer = MessageBox.Show(
                $"공유폴더 원본을 연결된 프로그램으로 여시겠습니까?\n\n{Path.GetFileName(path)}\n{Path.GetDirectoryName(path)}\n\n수정 후 저장하면 공유폴더 원본이 변경됩니다.",
                "CleanPotal 원본 열기",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "CleanPotal 실행 도우미",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static LauncherSettings LoadSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("실행 도우미 설정 파일(settings.json)이 없습니다. 다시 설치해 주세요.");

        var settings = JsonSerializer.Deserialize<LauncherSettings>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (settings is null || string.IsNullOrWhiteSpace(settings.ApiBaseUrl) || settings.AllowedRoots.Length == 0)
            throw new InvalidOperationException("실행 도우미 설정이 올바르지 않습니다.");
        return settings;
    }

    private static void ValidateServer(LauncherSettings settings)
    {
        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("API 서버 주소가 올바르지 않습니다.");

        var local = uri.IsLoopback && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !(local && settings.AllowInsecureLocalhost))
            throw new InvalidOperationException("운영 서버는 HTTPS만 사용할 수 있습니다.");
    }

    private static string ParseTicket(string rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("cleanpotal", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("신뢰할 수 없는 실행 주소입니다.");

        var ticket = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals("ticket", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .SingleOrDefault();

        if (ticket is null || ticket.Length != 64 || ticket.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidOperationException("실행권 형식이 올바르지 않습니다.");
        return ticket;
    }

    private static string ReadPath(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var wrapped) ? wrapped : root;
        if (!data.TryGetProperty("path", out var pathValue) || string.IsNullOrWhiteSpace(pathValue.GetString()))
            throw new InvalidOperationException("서버 응답에 파일 경로가 없습니다.");
        return pathValue.GetString()!;
    }

    private static string? ReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateDocument(string path, IReadOnlyCollection<string> allowedRoots)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath) || !File.Exists(fullPath))
            throw new InvalidOperationException("공유 파일이 없거나 접근할 수 없습니다.");

        var extension = Path.GetExtension(fullPath);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException($"보안을 위해 {extension} 파일은 직접 실행할 수 없습니다.");

        var withinAllowedRoot = allowedRoots.Any(root =>
        {
            var normalized = Path.GetFullPath(root.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(normalized + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
        if (!withinAllowedRoot)
            throw new InvalidOperationException("허용된 공유폴더 밖의 파일은 실행할 수 없습니다.");
    }

    private sealed record LauncherSettings(
        string ApiBaseUrl,
        string[] AllowedRoots,
        bool AllowInsecureLocalhost = false);
}
