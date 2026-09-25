using System.Text;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 콘솔 출력(Console.WriteLine 과 ASP.NET 콘솔 로그)을 날짜별 파일에도 남긴다.
///
/// IIS 안에서 돌면 콘솔이 없어 스키마 보강·MQTT 연결·예외 로그가 전부 사라졌다. 그래서 장애가 나도
/// "무엇이 먼저 실패했는지" 를 볼 수 없었다. 운영(개발환경이 아닐 때)에서는 기본으로 켠다.
///
/// 설정(appsettings.local.json, 모두 선택):
///   Logging:File:Enabled        true/false (기본: 개발환경이 아니면 true)
///   Logging:File:Path           폴더 (기본: 앱 폴더\App_Data\logs — 배포 때 보존하는 폴더)
///   Logging:File:RetentionDays  보관 일수 (기본 30, 0 이면 지우지 않음)
///
/// 파일에 쓰다 실패해도(디스크 가득·권한) 앱은 계속 돈다 — 그때부터 파일 쓰기만 멈춘다.
/// </summary>
public static class FileLog
{
    public const string FilePrefix = "portal-";

    /// <summary>켜졌으면 로그 폴더를, 아니면 null 을 돌려준다.</summary>
    public static string? Start(IConfiguration cfg, string contentRoot, bool isDevelopment)
    {
        var enabled = cfg.GetValue<bool?>("Logging:File:Enabled") ?? !isDevelopment;
        if (!enabled) return null;

        var dir = cfg["Logging:File:Path"];
        dir = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(contentRoot, "App_Data", "logs")
            : Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(contentRoot, dir));
        try
        {
            Directory.CreateDirectory(dir);
            Purge(dir, cfg.GetValue<int?>("Logging:File:RetentionDays") ?? 30, DateTime.Today);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[log][경고] 로그 폴더를 만들지 못해 파일 로그를 끕니다: {ex.Message}");
            return null;
        }

        Stop();
        var sink = new DailyFileSink(dir);
        _original = (Console.Out, Console.Error);
        _sink = sink;
        Console.SetOut(new TeeWriter(Console.Out, sink));
        Console.SetError(new TeeWriter(Console.Error, sink));
        return dir;
    }

    private static DailyFileSink? _sink;
    private static (TextWriter Out, TextWriter Error)? _original;

    /// <summary>원래 콘솔로 되돌리고 파일을 닫는다(테스트·재시작용).</summary>
    public static void Stop()
    {
        if (_original is { } o)
        {
            Console.SetOut(o.Out);
            Console.SetError(o.Error);
            _original = null;
        }
        _sink?.Dispose();
        _sink = null;
    }

    /// <summary>보관 기간이 지난 로그 파일을 지운다(이름의 날짜 기준).</summary>
    public static int Purge(string dir, int retentionDays, DateTime today)
    {
        if (retentionDays <= 0 || !Directory.Exists(dir)) return 0;
        var cutoff = today.Date.AddDays(-retentionDays);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(dir, FilePrefix + "*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (!DateTime.TryParseExact(stamp, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day)) continue;
            if (day >= cutoff) continue;
            try { File.Delete(path); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    /// <summary>하루 한 파일. 줄 앞에 시각을 붙인다. 여러 스레드가 동시에 써도 줄이 섞이지 않게 잠근다.</summary>
    internal sealed class DailyFileSink : IDisposable
    {
        private readonly string _dir;
        private readonly object _gate = new();
        private StreamWriter? _writer;
        private DateTime _day;
        private bool _atLineStart = true;
        private bool _broken;

        public DailyFileSink(string dir) => _dir = dir;

        public void Write(string text)
        {
            if (_broken || text.Length == 0) return;
            lock (_gate)
            {
                try
                {
                    var now = DateTime.Now;
                    var writer = WriterFor(now);
                    var sb = new StringBuilder(text.Length + 32);
                    foreach (var ch in text)
                    {
                        if (_atLineStart && ch != '\r' && ch != '\n')
                        {
                            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ');
                            _atLineStart = false;
                        }
                        sb.Append(ch);
                        if (ch == '\n') _atLineStart = true;
                    }
                    // 재시작이 겹치거나 명령줄 도구가 같은 파일에 쓰면 열 때의 끝 위치는 이미 지난 자리다 —
                    // 쓸 때마다 끝으로 옮겨 남의 줄을 덮어쓰지 않게 한다.
                    writer.BaseStream.Seek(0, SeekOrigin.End);
                    writer.Write(sb.ToString());
                    writer.Flush();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    _broken = true;
                    try { _writer?.Dispose(); } catch { /* 이미 망가진 파일 */ }
                    _writer = null;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _broken = true;
                try { _writer?.Dispose(); } catch { /* 닫는 중 오류는 무시 */ }
                _writer = null;
            }
        }

        private StreamWriter WriterFor(DateTime now)
        {
            if (_writer is null || now.Date != _day)
            {
                _writer?.Dispose();
                _day = now.Date;
                var path = Path.Combine(_dir, $"{FilePrefix}{_day:yyyyMMdd}.log");
                // 다른 프로세스(재시작 중 겹친 이전 워커)가 같은 파일을 열고 있어도 열 수 있게 공유한다.
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                // 새 파일 맨 앞에 BOM 을 붙인다 — 없으면 윈도우 PowerShell 5 의 Get-Content·메모장 일부가
                // 한글을 CP949 로 읽어 깨진다. 이미 내용이 있는 파일에 이어 쓸 때는 붙지 않는다.
                _writer = new StreamWriter(stream, new UTF8Encoding(true));
            }
            return _writer;
        }
    }

    /// <summary>원래 콘솔과 파일에 같이 쓴다.</summary>
    internal sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly DailyFileSink _sink;

        public TeeWriter(TextWriter inner, DailyFileSink sink)
        {
            _inner = inner;
            _sink = sink;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            _sink.Write(value.ToString());
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            _inner.Write(value);
            _sink.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            _inner.Write(buffer, index, count);
            _sink.Write(new string(buffer, index, count));
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            _sink.Write((value ?? "") + Environment.NewLine);
        }

        public override void WriteLine()
        {
            _inner.WriteLine();
            _sink.Write(Environment.NewLine);
        }

        public override void Flush() => _inner.Flush();
    }
}
