using System.Windows;

namespace KubeTail;

public partial class App : Application
{
    private static bool _errorShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_errorShown) return;
            _errorShown = true;
            System.Diagnostics.Debug.WriteLine($"Error: {args.Exception}");
        };
    }
}
