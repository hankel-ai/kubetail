using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KubeTail.Models;
using KubeTail.Services;
using KubeTail.ViewModels;

namespace KubeTail.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;
    private readonly ConfigService _config = new();
    private int _spinIdx;
    private static readonly string[] Spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public MainWindow()
    {
        InitializeComponent();

        // Restore window state
        var cfg = _config.Load();
        if (cfg.IsMaximized) WindowState = WindowState.Maximized;
        if (cfg.WindowWidth > 0) Width = cfg.WindowWidth;
        if (cfg.WindowHeight > 0) Height = cfg.WindowHeight;

        // Pre-select last used profile in the combo box
        if (!string.IsNullOrEmpty(VM.CurrentProfileName))
        {
            var match = VM.SavedProfiles.FirstOrDefault(p => p.ProfileName == VM.CurrentProfileName);
            if (match != null) ProfileCombo.SelectedItem = match;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            var tab = VM.SelectedTab;
            if (tab?.IsStreaming == true)
            {
                _spinIdx = (_spinIdx + 1) % Spin.Length;
                SpinnerRun.Text = Spin[_spinIdx] + " ";
            }
            else
                SpinnerRun.Text = "";
        };
        timer.Start();

        Closing += (_, _) =>
        {
            var c = _config.Load();
            c.IsMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                c.WindowWidth = Width;
                c.WindowHeight = Height;
            }
            _config.Save(c);
        };
    }

    private void CloseTab(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabViewModel tab)
            VM.CloseTabCommand.Execute(tab);
    }

    private void OpenClusterManager(object sender, RoutedEventArgs e)
    {
        var dlg = new ClusterManagerDialog(VM.Clusters) { Owner = this };
        if (dlg.ShowDialog() == true)
            VM.SaveAllClusters();
    }

    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("Save Profile", "Profile name:", VM.CurrentProfileName) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            VM.SaveProfileCommand.Execute(dlg.InputText);
            var match = VM.SavedProfiles.FirstOrDefault(p => p.ProfileName == dlg.InputText);
            if (match != null) ProfileCombo.SelectedItem = match;
        }
    }

    private void LoadProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is SavedTabProfile profile)
            VM.LoadProfileCommand.Execute(profile);
    }

    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is SavedTabProfile profile)
        {
            if (MessageBox.Show($"Delete profile '{profile.ProfileName}'?", "Confirm",
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                VM.DeleteProfileCommand.Execute(profile);
        }
    }
}
