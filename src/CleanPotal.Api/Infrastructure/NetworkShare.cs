using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 공유폴더(NAS)에 계정으로 접속한다.
///
/// IIS 앱 풀 기본 계정은 NAS 가 모르는 계정이라 공유폴더에 쓸 수 없다. 그래서 설정에 적은 NAS 계정으로
/// 이 프로세스가 직접 연결을 연다(드라이브 문자 없이 — "net use \\NAS\공유 /user:계정" 과 같다).
/// 한 번 열면 같은 공유 아래 경로(첨부·MES 문서)는 모두 그 계정으로 읽고 쓴다.
///
///   Storage:AttachmentsPath   첨부 저장 위치(\\NAS\공유\...)
///   Storage:ShareUser         NAS 계정 — 비어 있으면 연결을 열지 않는다(지금 계정으로 되는 곳이면 필요 없다)
///   Storage:SharePassword     NAS 비밀번호 — appsettings.local.json 에만 둔다. 로그에 찍지 않는다.
///   MesData:RootPath          MES 문서 위치 — 같은 NAS 공유면 같은 계정으로 열린다.
/// </summary>
public static class NetworkShare
{
    private static readonly object Gate = new();
    private static readonly List<string> Shares = [];
    private static string _user = "";
    private static string _password = "";

    public static void Configure(IConfiguration cfg)
    {
        _user = (cfg["Storage:ShareUser"] ?? "").Trim();
        _password = cfg["Storage:SharePassword"] ?? "";
        Shares.Clear();
        if (_user.Length == 0) return;

        foreach (var path in new[] { cfg["Storage:AttachmentsPath"], cfg["MesData:RootPath"] })
        {
            var share = ShareRoot(path);
            if (share is not null && !Shares.Contains(share, StringComparer.OrdinalIgnoreCase)) Shares.Add(share);
        }
        foreach (var share in Shares) Connect(share);
    }

    /// <summary>\\서버\공유\아래\경로 → \\서버\공유. 공유폴더 경로가 아니면 null.</summary>
    public static string? ShareRoot(string? path)
    {
        path = (path ?? "").Trim();
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : $@"\\{parts[0]}\{parts[1]}";
    }

    /// <summary>
    /// 경로가 안 보이면 연결을 다시 연다(NAS 재시작·네트워크 끊김 뒤). 설정한 공유 밑이 아니면 아무것도 안 한다.
    /// </summary>
    public static void EnsureReachable(string path)
    {
        var share = ShareRoot(path);
        if (share is null || !Shares.Contains(share, StringComparer.OrdinalIgnoreCase)) return;
        if (Directory.Exists(path) || Directory.Exists(share)) return;
        Connect(share);
    }

    private static void Connect(string share)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[storage] 공유폴더 계정 접속은 윈도우에서만 한다: {share}");
            return;
        }
        lock (Gate)
        {
            // 이미 보이면(이 계정으로 이미 열려 있거나, 지금 계정으로도 되는 곳) 새로 열지 않는다.
            if (Directory.Exists(share))
            {
                Console.WriteLine($"[storage] 공유폴더 접근 가능: {share}");
                return;
            }
            var res = new NetResource { dwType = ResourceTypeDisk, lpRemoteName = share };
            var code = WNetAddConnection2(ref res, _password, _user, 0);
            Console.WriteLine(code switch
            {
                0 => $"[storage] 공유폴더 연결: {share} (계정 {_user})",
                // 테스트 서버처럼 사람이 쓰는 PC 에서 다른 계정으로 이미 연결해 둔 경우 — 그 연결을 끊지 않는다.
                1219 => $"[storage][경고] {share} 에 다른 계정으로 이미 연결되어 있어 그 연결을 그대로 쓴다.",
                1326 => $"[storage][오류] {share} 접속 실패: NAS 계정 또는 비밀번호가 틀립니다(계정 {_user}).",
                53 or 67 => $"[storage][오류] {share} 접속 실패: 공유폴더를 찾을 수 없습니다(주소·공유 이름 확인).",
                5 => $"[storage][오류] {share} 접속 실패: 이 계정에 공유폴더 권한이 없습니다(계정 {_user}).",
                _ => $"[storage][오류] {share} 접속 실패({code}): {new Win32Exception(code).Message}",
            });
        }
    }

    private const int ResourceTypeDisk = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? username, int flags);
}
