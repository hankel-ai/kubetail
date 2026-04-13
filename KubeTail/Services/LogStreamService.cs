using KubeTail.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace KubeTail.Services;

public partial class LogStreamService : IDisposable
{
    private readonly KubeService _kube;
    private readonly BufferService _buffer;
    private readonly Dictionary<string, CancellationTokenSource> _streams = new();
    private readonly object _lock = new();

    public LogSettings Settings { get; set; } = new();
    public event Action<string>? OnError;
    public event Action? OnAllCompleted;
    public int ActiveCount { get { lock (_lock) { return _streams.Count; } } }

    public LogStreamService(KubeService kube, BufferService buffer)
    {
        _kube = kube; _buffer = buffer;
    }

    public async Task StartAsync(List<LogSource> sources, CancellationToken ct)
    {
        StopAll();
        foreach (var src in sources)
        {
            try
            {
                var pods = await _kube.GetPodsForControllerAsync(
                    src.Namespace, src.ControllerKind, src.ControllerName, ct);
                foreach (var pod in pods)
                    StartPodTail(src, pod, ct);
            }
            catch (Exception ex) { OnError?.Invoke($"Start failed {src.Key}: {ex.Message}"); }
        }
    }

    private void StartPodTail(LogSource src, string pod, CancellationToken ct)
    {
        var key = $"{src.Namespace}/{pod}/{src.ContainerName}";
        lock (_lock)
        {
            if (_streams.ContainsKey(key)) return;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _streams[key] = cts;
            _ = TailAsync(src, pod, key, cts.Token);
        }
    }

    private async Task TailAsync(LogSource src, string pod, string key, CancellationToken ct)
    {
        var delay = 1000;
        while (!ct.IsCancellationRequested)
        {
            bool streamEndedNaturally = false;
            try
            {
                using var stream = await _kube.StreamPodLogAsync(
                    src.Namespace, pod, src.ContainerName, Settings, ct);
                using var reader = new StreamReader(stream);
                delay = 1000;
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) { streamEndedNaturally = true; break; }
                    _buffer.Append(ParseLine(line, src, pod));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* pod gone or stream broken — will retry */ }

            if (ct.IsCancellationRequested) break;

            // If not following, stream ends naturally — don't retry
            if (!Settings.Follow && streamEndedNaturally) break;

            // Stream broke — pod likely died. Re-resolve from controller immediately.
            try { await Task.Delay(delay, ct); } catch { break; }
            delay = Math.Min(delay * 2, 15_000);

            try
            {
                var pods = await _kube.GetPodsForControllerAsync(
                    src.Namespace, src.ControllerKind, src.ControllerName, ct);

                if (pods.Count == 0) continue; // no pods yet, loop will retry

                // Start tailing any new pods we aren't already tracking
                foreach (var p in pods)
                    StartPodTail(src, p, ct);

                // If our original pod is gone, stop this loop
                if (!pods.Contains(pod))
                {
                    lock (_lock) { _streams.Remove(key); }
                    NotifyIfAllDone(ct);
                    return;
                }
                // If our pod is still there, loop retries the stream on it
            }
            catch { /* can't reach API, keep retrying */ }
        }
        lock (_lock) { _streams.Remove(key); }
        NotifyIfAllDone(ct);
    }

    private void NotifyIfAllDone(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        bool fire;
        lock (_lock) { fire = _streams.Count == 0; }
        if (fire) OnAllCompleted?.Invoke();
    }

    [GeneratedRegex(@"^\[([^/]+)/([^\]]+)\]\s*(.*)$")]
    private static partial Regex PrefixRx();
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}T[\d:.]+Z?)\s+(.*)")]
    private static partial Regex TsRx();

    private static LogEntry ParseLine(string line, LogSource src, string pod)
    {
        var entry = new LogEntry
        {
            RawLine = line, Namespace = src.Namespace, Pod = pod,
            Container = src.ContainerName, Controller = src.ControllerName,
            ControllerKind = src.ControllerKind, Timestamp = DateTime.UtcNow, Message = line
        };
        var rest = line;
        var pm = PrefixRx().Match(rest);
        if (pm.Success) { entry.Pod = pm.Groups[1].Value; entry.Container = pm.Groups[2].Value; rest = pm.Groups[3].Value; }
        var tm = TsRx().Match(rest);
        if (tm.Success)
        {
            if (DateTime.TryParse(tm.Groups[1].Value, out var ts)) entry.Timestamp = ts;
            entry.Message = tm.Groups[2].Value;
        }
        else entry.Message = rest;
        return entry;
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var cts in _streams.Values)
                try { cts.Cancel(); cts.Dispose(); } catch { }
            _streams.Clear();
        }
    }

    public void Dispose() => StopAll();
}
