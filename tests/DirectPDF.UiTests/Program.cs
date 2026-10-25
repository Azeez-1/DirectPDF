using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DirectPDF.App;
using DirectPDF.Core;

namespace DirectPDF.UiTests;

internal static class Program
{
    private static int _exitCode = 1;
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new DirectPDF.App.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
        window.Show();
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try { await Exercise(window, args); _exitCode = 0; Console.WriteLine("PASS WPF open/select/preview/apply/undo/redo/navigate/zoom/save/close"); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                if (_exitCode != 0) Environment.Exit(_exitCode);
                app.Shutdown(_exitCode);
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        return _exitCode;
    }

    private static async Task Exercise(MainWindow window, string[] args)
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "ui-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        T Control<T>(string name) where T : class => window.FindName(name) as T ?? throw new Exception("Missing control: " + name);
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Click(string name)
        {
            var button = Control<Button>(name);
            Check(button.IsEnabled, name + " is disabled");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        async Task WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception("UI timeout: " + Control<TextBlock>("EditorMessage").Text); await Task.Delay(30); }
        }
        async Task Idle() => await WaitFor(() => Control<Border>("BusyOverlay").Visibility != Visibility.Visible);
        async Task Screenshot(string name)
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(output);
        }

        Check(Control<Grid>("WelcomePanel").Visibility == Visibility.Visible, "Welcome screen");
        await Screenshot("welcome");
        var input = args.Length > 0 ? args[0] : Path.GetFullPath("samples/Welcome.pdf");
        var originalText = args.Length > 1 ? args[1] : "Status: Waiting for final review";
        var newText = args.Length > 2 ? args[2] : "Status: Ready";
        await window.OpenPathAsync(input);
        Check(Control<Grid>("EditorPanel").Visibility == Visibility.Visible, "Editor opened");
        var list = Control<ListBox>("SegmentsList");
        var segment = list.Items.Cast<TextSegment>().Single(s => s.Text == originalText);
        list.SelectedItem = segment;
        var replacement = Control<TextBox>("ReplacementBox");
        Check(replacement.IsEnabled, "Text input enabled");
        Check(Math.Abs(replacement.FontSize - segment.FontSize * 96.0 / 72) < .001, "Typing inherits PDF font size");
        if (segment.FontFamily == "Times New Roman") Check(replacement.FontFamily.Source == "Times New Roman", "Typing inherits Times New Roman");
        Check(replacement.FontWeight == (segment.IsBold ? FontWeights.Bold : FontWeights.Normal), "Typing inherits font weight");
        Check(replacement.FontStyle == (segment.IsItalic ? FontStyles.Italic : FontStyles.Normal), "Typing inherits font style");
        replacement.Text = newText;
        Click("PreviewButton");
        await Idle();
        Check(Control<Button>("ApplyButton").IsEnabled, "Preview must pass: " + Control<TextBlock>("EditorMessage").Text);
        Check(!replacement.IsEnabled, "Preview freezes replacement text");
        Check(!Control<Button>("SaveButton").IsEnabled, "Unapplied preview cannot save");
        await Screenshot("preview");
        Click("ApplyButton");
        Check(list.Items.Cast<TextSegment>().Any(s => s.Text == newText), "Applied text");
        Check(window.Title.StartsWith("* ", StringComparison.Ordinal), "Dirty title");
        Click("UndoButton"); await Idle();
        Check(list.Items.Cast<TextSegment>().Any(s => s.Text == originalText), "Undo via UI");
        Click("RedoButton"); await Idle();
        Check(list.Items.Cast<TextSegment>().Any(s => s.Text == newText), "Redo via UI");
        Click("NextButton"); await Idle();
        Check(Control<TextBlock>("PageLabel").Text.StartsWith("2 / ", StringComparison.Ordinal), "Page navigation");
        Click("PreviousButton"); await Idle();
        Click("ZoomOutButton"); await Idle();
        Check(Control<TextBlock>("ZoomLabel").Text.Contains("70"), "Zoom changed");
        var saved = Path.Combine(directory, "ui-edited.pdf");
        Check(await window.SaveCopyAsync(saved), "Save from the UI");
        Check(!window.Title.StartsWith("* ", StringComparison.Ordinal), "Saved title");
        Check(PdfEditor.Open(saved).ReadPage(0).Segments.Any(s => s.Text == newText), "UI-saved text reopens");
        await Screenshot("editor");
        window.Close();
        await WaitFor(() => !window.IsVisible);
        Console.WriteLine("UI artifacts: " + directory);
    }
}
