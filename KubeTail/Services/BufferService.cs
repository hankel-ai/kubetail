using KubeTail.Models;
using System.IO;

namespace KubeTail.Services;

public class BufferService : IDisposable
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();
    private readonly Queue<LogEntry> _pending = new();
    private StreamWriter? _spillWriter;
    private string? _spillPath;
    private int _spilledCount;

    public BufferMode Mode { get; set; } = BufferMode.Unlimited;
    public int RollingMaxLines { get; set; } = 100_000;
    public int SpillThreshold { get; set; } = 500_000;
    public int TotalCount => _spilledCount + _entries.Count;

    /// <summary>Drain pending entries and return them as a batch. Call from UI thread.</summary>
    public List<LogEntry> DrainBatch()
    {
        List<LogEntry> batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return new(0);
            batch = new(_pending);
            _pending.Clear();
        }
        foreach (var e in batch)
        {
            _entries.Add(e);
            if (Mode == BufferMode.Rolling && _entries.Count > RollingMaxLines)
                _entries.RemoveAt(0);
            else if (Mode == BufferMode.Unlimited && _entries.Count > SpillThreshold)
            {
                SpillToDisk(_entries[0]);
                _entries.RemoveAt(0);
            }
        }
        return batch;
    }

    public void Append(LogEntry entry)
    {
        lock (_lock) { _pending.Enqueue(entry); }
    }

    private void SpillToDisk(LogEntry e)
    {
        if (_spillWriter == null)
        {
            _spillPath = Path.Combine(Path.GetTempPath(), $"kubetail_{Guid.NewGuid():N}.log");
            _spillWriter = new StreamWriter(_spillPath, true) { AutoFlush = false };
        }
        _spillWriter.WriteLine(FormatEntry(e));
        _spilledCount++;
        if (_spilledCount % 1000 == 0) _spillWriter.Flush();
    }

    public IEnumerable<string> GetAllLinesIncludingSpill()
    {
        if (_spillPath != null && File.Exists(_spillPath))
        {
            _spillWriter?.Flush();
            foreach (var line in File.ReadLines(_spillPath)) yield return line;
        }
        foreach (var e in _entries) yield return FormatEntry(e);
    }

    public IEnumerable<LogEntry> GetAllEntries() => _entries;

    public void Clear()
    {
        lock (_lock) { _pending.Clear(); }
        _entries.Clear();
        _spillWriter?.Dispose(); _spillWriter = null;
        if (_spillPath != null) try { File.Delete(_spillPath); } catch { }
        _spillPath = null; _spilledCount = 0;
    }

    private static string FormatEntry(LogEntry e) =>
        $"{e.Timestamp:O}\t{e.Namespace}\t{e.Pod}\t{e.Container}\t{e.Message}";

    public void Dispose()
    {
        _spillWriter?.Dispose();
        if (_spillPath != null) try { File.Delete(_spillPath); } catch { }
    }
}
