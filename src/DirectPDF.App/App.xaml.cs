using System.Windows;

namespace DirectPDF.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (MainWindow != null) return;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        var file = e.Args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
        if (file != null) window.Loaded += async (_, _) => await window.OpenPathAsync(file);
    }
}
