using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KubeTail.Models;
using KubeTail.ViewModels;

namespace KubeTail.Views;

public partial class LogTabView : UserControl
{
    private TabViewModel? _vm;
    private DispatcherTimer? _toastTimer;
    private int _dragStartIndex = -1;
    private int _lastDragIndex = -1;
    private bool _scrollPending;

    public LogTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null) window.Deactivated += (_, _) => CloseAllPopups();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == DataContextProperty && DataContext is TabViewModel vm)
        {
            _vm = vm;
            vm.FilteredEntries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm?.IsFollowing != true || LogList.Items.Count == 0) return;
        if (_scrollPending) return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _scrollPending = false;
            try
            {
                var sv = GetScrollViewer(LogList);
                sv?.ScrollToEnd();
            }
            catch { }
        });
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject o)
    {
        if (o is ScrollViewer sv) return sv;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var r = GetScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
            if (r != null) return r;
        }
        return null;
    }

    private void LogList_PreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (AnyPopupOpen()) { CloseAllPopups(); return; }

        // Track start for drag-select (only without Shift/Ctrl so those still work normally)
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            _dragStartIndex = GetItemIndexAtPoint(e.GetPosition(LogList));
            _lastDragIndex = _dragStartIndex;
        }
        else
        {
            _dragStartIndex = -1;
        }
    }

    private void LogList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return;

        var currentIdx = GetItemIndexAtPoint(e.GetPosition(LogList));
        if (currentIdx < 0 || currentIdx == _lastDragIndex) return;
        _lastDragIndex = currentIdx;

        int start = Math.Min(_dragStartIndex, currentIdx);
        int end = Math.Max(_dragStartIndex, currentIdx);

        LogList.SelectedItems.Clear();
        for (int i = start; i <= end && i < LogList.Items.Count; i++)
            LogList.SelectedItems.Add(LogList.Items[i]);
    }

    private void LogList_PreviewLeftUp(object sender, MouseButtonEventArgs e)
    {
        _dragStartIndex = -1;
        _lastDragIndex = -1;
    }

    private int GetItemIndexAtPoint(Point point)
    {
        var hit = VisualTreeHelper.HitTest(LogList, point);
        if (hit?.VisualHit == null) return -1;
        DependencyObject obj = hit.VisualHit;
        while (obj != null && obj != LogList)
        {
            if (obj is ListBoxItem item)
                return LogList.ItemContainerGenerator.IndexFromContainer(item);
            obj = VisualTreeHelper.GetParent(obj);
        }
        return -1;
    }

    private void LogList_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (AnyPopupOpen()) CloseAllPopups();
        // Prevent right-click from changing selection so multi-select + right-click copy works
        e.Handled = true;
    }

    private void LogList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (LogList.SelectedItems.Count == 0) return;
        var count = LogList.SelectedItems.Count;
        var sb = new StringBuilder();
        foreach (var item in LogList.SelectedItems)
        {
            if (item is LogEntry entry)
            {
                if (_vm?.OptPrefix == true) sb.Append(entry.SourceTag).Append(' ');
                if (_vm?.OptTimestamps == true) sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ');
                sb.AppendLine(entry.Message);
            }
        }
        try { Clipboard.SetText(sb.ToString()); } catch { }
        LogList.UnselectAll();
        ShowToast($"Copied {count} line{(count == 1 ? "" : "s")}");
        e.Handled = true;
    }

    private void ShowToast(string message, int durationMs = 1500)
    {
        ToastText.Text = message;
        ToastOverlay.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastOverlay.Visibility = Visibility.Collapsed; };
        _toastTimer.Start();
    }

    private void ScrollToTop(object sender, RoutedEventArgs e) => GetScrollViewer(LogList)?.ScrollToHome();
    private void ScrollToBottom(object sender, RoutedEventArgs e) => GetScrollViewer(LogList)?.ScrollToEnd();

    private void LogList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_vm == null || e.OriginalSource is not ScrollViewer sv) return;
        var atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 10;
        _vm.IsAtBottom = atBottom;
        BottomIndicator.Text = atBottom ? "● BOTTOM" : "○ NOT BOTTOM";
        BottomIndicator.Foreground = atBottom
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 170, 0))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 0));
    }

    private void ConfigureSources(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var mainVm = (MainViewModel)Window.GetWindow(this)!.DataContext;
        var dlg = new ConfigureSourcesDialog(_vm.KubeService, mainVm.Clusters,
            _vm.Sources.ToList()) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            if (_vm.IsStreaming) _vm.StopStreamingCommand.Execute(null);
            _vm.Sources.Clear();
            foreach (var s in dlg.SelectedSources) _vm.Sources.Add(s);
            _vm.CurrentCluster = dlg.SelectedCluster;
            if (_vm.Sources.Count > 0)
                _vm.StartStreamingCommand.Execute(null);
        }
    }

    private void AddHideWord(object sender, RoutedEventArgs e) { _vm?.AddHideWordCommand.Execute(HideWordBox.Text.Trim()); HideWordBox.Clear(); }
    private void HideWord_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddHideWord(sender, e); e.Handled = true; } }
    private void AddHighlightWord(object sender, RoutedEventArgs e) { _vm?.AddHighlightWordCommand.Execute(HighlightWordBox.Text.Trim()); HighlightWordBox.Clear(); }
    private void HighlightWord_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddHighlightWord(sender, e); e.Handled = true; } }

    private void ToggleNsPopup(object s, MouseButtonEventArgs e) { bool was = NsPopup.IsOpen; CloseAllPopups(); if (!was) NsPopup.IsOpen = true; e.Handled = true; }
    private void ToggleCtrlPopup(object s, MouseButtonEventArgs e) { bool was = CtrlPopup.IsOpen; CloseAllPopups(); if (!was) CtrlPopup.IsOpen = true; e.Handled = true; }
    private void TogglePodPopup(object s, MouseButtonEventArgs e) { bool was = PodPopup.IsOpen; CloseAllPopups(); if (!was) PodPopup.IsOpen = true; e.Handled = true; }
    private void CloseAllPopups() { NsPopup.IsOpen = false; CtrlPopup.IsOpen = false; PodPopup.IsOpen = false; }

    // Only close popups when clicking outside them — don't intercept all mouse events
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;
        if (AnyPopupOpen() && !IsClickInsidePopup(e))
            CloseAllPopups();
    }

    private bool AnyPopupOpen() => NsPopup.IsOpen || CtrlPopup.IsOpen || PodPopup.IsOpen;

    private bool IsClickInsidePopup(MouseButtonEventArgs e)
    {
        return IsOverPopup(NsPopup, e) || IsOverPopup(CtrlPopup, e) || IsOverPopup(PodPopup, e)
            || IsOverElement(NsLabel, e) || IsOverElement(CtrlLabel, e) || IsOverElement(PodLabel, e);
    }

    private static bool IsOverPopup(System.Windows.Controls.Primitives.Popup p, MouseButtonEventArgs e)
    {
        if (p.Child == null || !p.IsOpen) return false;
        var pos = e.GetPosition(p.Child);
        var fe = (FrameworkElement)p.Child;
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= fe.ActualWidth && pos.Y <= fe.ActualHeight;
    }

    private static bool IsOverElement(FrameworkElement el, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(el);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= el.ActualWidth && pos.Y <= el.ActualHeight;
    }

    private void ExportLog(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Log files (*.log)|*.log|CSV files (*.csv)|*.csv|All (*.*)|*.*",
            FileName = $"kubetail-{_vm.Name}-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var isCsv = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            using var w = new StreamWriter(dlg.FileName);
            if (isCsv) w.WriteLine("Timestamp,Namespace,Pod,Container,Message");
            foreach (var line in _vm.GetFilteredLines())
            {
                if (isCsv)
                {
                    var p = line.Split('\t', 5);
                    w.WriteLine(p.Length >= 5
                        ? $"\"{p[0]}\",\"{p[1]}\",\"{p[2]}\",\"{p[3]}\",\"{p[4].Replace("\"", "\"\"")}\""
                        : $"\"{line.Replace("\"", "\"\"")}\"");
                }
                else w.WriteLine(line);
            }
            _vm.StatusText = $"✓ Exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { _vm.StatusText = $"Export failed: {ex.Message}"; }
    }
}
