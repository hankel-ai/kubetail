using k8s;
using KubeTail.Models;
using System.IO;
using System.Net.WebSockets;
using System.Text;

namespace KubeTail.Services;

public class SmartLogStreamService : IDisposable
{
    private readonly KubeService _kube;
    private readonly BufferService _buffer;
    private readonly Dictionary<string, CancellationTokenSource> _streams = new();
    private readonly object _lock = new();

    public event Action<string>? OnError;

    public SmartLogStreamService(KubeService kube, BufferService buffer)
    {
        _kube = kube;
        _buffer = buffer;
    }

    public void StartStream(SmartLogDefinition def, LogSource src, string pod, CancellationToken ct)
    {
        var key = $"smartlog:{src.Namespace}/{pod}/{src.ContainerName}/{def.Id}";
        lock (_lock)
        {
            if (_streams.ContainsKey(key)) return;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _streams[key] = cts;
            _ = TailAsync(def, src, pod, key, cts.Token);
        }
    }

    public void StopStream(SmartLogDefinition def, LogSource src, string pod)
    {
        var key = $"smartlog:{src.Namespace}/{pod}/{src.ContainerName}/{def.Id}";
        lock (_lock)
        {
            if (_streams.Remove(key, out var cts))
            {
                try { cts.Cancel(); cts.Dispose(); } catch { }
            }
        }
    }

    private async Task TailAsync(SmartLogDefinition def, LogSource src, string pod,
        string key, CancellationToken ct)
    {
        var delay = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Build the tail command — use shell wrapping for wildcard support
                var path = def.LogFilePath;
                string[] command;
                if (path.Contains('*') || path.Contains('?'))
                    command = new[] { "sh", "-c", $"tail -F {path}" };
                else
                    command = new[] { "tail", "-F", path };

                using var ws = await _kube.ExecInPodAsync(
                    src.Namespace, pod, src.ContainerName, command, ct);

                var demuxer = new StreamDemuxer(ws);
                demuxer.Start();

                // Read stdout (channel 1)
                using var stdout = demuxer.GetStream(1, 0);
                using var reader = new StreamReader(stdout, Encoding.UTF8);

                delay = 1000;
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    _buffer.Append(new LogEntry
                    {
                        Namespace = src.Namespace,
                        Pod = pod,
                        Container = src.ContainerName,
                        Controller = src.ControllerName,
                        ControllerKind = src.ControllerKind,
                        Message = line,
                        RawLine = line,
                        Timestamp = DateTime.UtcNow,
                        IsSmartLog = true,
                        SmartLogDescription = def.Description,
                        SmartLogFilePath = def.LogFilePath
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                OnError?.Invoke($"SmartLog {def.Description} on {pod}: {ex.Message}");
            }

            if (ct.IsCancellationRequested) break;

            try { await Task.Delay(delay, ct); } catch { break; }
            delay = Math.Min(delay * 2, 30_000);
        }

        lock (_lock) { _streams.Remove(key); }
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
