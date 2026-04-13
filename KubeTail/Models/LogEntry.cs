using CommunityToolkit.Mvvm.ComponentModel;

namespace KubeTail.Models;

public partial class LogEntry : ObservableObject
{
    public DateTime Timestamp { get; set; }
    public string Namespace { get; set; } = "";
    public string Pod { get; set; } = "";
    public string Container { get; set; } = "";
    public string Controller { get; set; } = "";
    public string ControllerKind { get; set; } = "";
    public string Message { get; set; } = "";
    public string RawLine { get; set; } = "";
    public string SourceTag => $"[{Namespace}/{Controller}/{Pod}/{Container}]";
    public string PodContainer => $"{Pod}/{Container}";
    public string ControllerKey => $"{ControllerKind}/{Controller}";
    public string ControllerContainer => $"{Controller}/{Container}";
    public DateTime ArrivalTime { get; set; } = DateTime.UtcNow;

    // SmartLog properties
    public bool IsSmartLog { get; set; }
    public string SmartLogDescription { get; set; } = "";
    public string SmartLogFilePath { get; set; } = "";

    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private bool _isNew = true;
}
