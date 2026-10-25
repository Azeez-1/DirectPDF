using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DirectPDF.Core;
using Microsoft.Win32;

namespace DirectPDF.App;

public partial class MainWindow : Window
{
    private PdfEditor? _editor;
    private PageView? _view;
    private TextSegment? _selected;
    private EditPreview? _preview;
    private int _pageIndex;
    private double _zoom = .85;
    private bool _busy;
    private bool _updating;
    private bool _closing;
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(23, 107, 91));

    public MainWindow()
    {
        InitializeComponent();
        UpdateControls();
    }

    public async Task OpenPathAsync(string path)
    {
        if (_busy || !await MayLeaveDocumentAsync()) return;
        await RunBusyAsync("Opening your PDF…", async () =>
        {
            var loaded = await Task.Run(() =>
            {
                var editor = PdfEditor.Open(path);
                return (Editor: editor, View: editor.ReadPage(0, RenderScale));
            });
            _editor = loaded.Editor;
            _pageIndex = 0;
            _view = loaded.View;
            _preview = null;
            ResetSelection();
            WelcomePanel.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility = Visibility.Visible;
            ShowPage(_view, true);
            FileLabel.Text = Path.GetFileName(_editor.SourcePath);
            FileLabel.ToolTip = _editor.SourcePath;
            StatusLabel.Text = "Select a text segment to edit.";
        });
    }

    private double RenderScale => _zoom * 96 / 72;
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) await OpenPathAsync(dialog.FileName);
    }
    private async void Sample_Click(object sender, RoutedEventArgs e) => await OpenPathAsync(Path.Combine(AppContext.BaseDirectory, "samples", "Welcome.pdf"));

    private void ShowPage(PageView view, bool resetScroll = false)
    {
        var bitmap = view.Bitmap;
        var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, bitmap.Pixels, bitmap.Stride);
        source.Freeze();
        PageImage.Source = source;
        PageSurface.Width = bitmap.Width;
        PageSurface.Height = bitmap.Height;
        SelectionCanvas.Width = bitmap.Width;
        SelectionCanvas.Height = bitmap.Height;
        _updating = true;
        SegmentsList.ItemsSource = view.Segments;
        SegmentsList.SelectedItem = _selected == null ? null : view.Segments.FirstOrDefault(s => s.ObjectIndex == _selected.ObjectIndex);
        _updating = false;
        DrawSelection(view);
        PageLabel.Text = $"{view.PageIndex + 1} / {view.PageCount}";
        ZoomLabel.Text = $"{_zoom:P0}";
        PreviewBanner.Visibility = _preview == null ? Visibility.Collapsed : Visibility.Visible;
        var editable = view.Segments.Count(s => s.CanEdit);
        SegmentCount.Text = view.Segments.Count == 0
            ? "No directly editable text found. This page may be scanned, outlined, or use grouped content."
            : $"{view.Segments.Count} text segments · {editable} available for validation";
        if (_selected == null) SetMessage(view.Restriction ?? (view.Segments.Count == 0
            ? "This page can be viewed, but it has no supported text segments. OCR is planned for a later version."
            : "Select a text segment. A preview checks whether its font and layout can be preserved."));
        if (resetScroll) { PageScroll.ScrollToTop(); PageScroll.ScrollToLeftEnd(); }
    }

    private void DrawSelection(PageView view)
    {
        SelectionCanvas.Children.Clear();
        if (_preview != null) return;
        foreach (var segment in view.Segments)
        {
            var selected = _selected?.ObjectIndex == segment.ObjectIndex;
            var rect = segment.DisplayBounds;
            var border = new Border
            {
                Width = Math.Max(4, rect.Width + 6), Height = Math.Max(7, rect.Height + 6),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected ? Green : Brushes.Transparent,
                Background = selected ? new SolidColorBrush(Color.FromArgb(30, 23, 107, 91)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = segment.CanEdit ? "Select to edit: " + segment.Text : segment.Restriction
            };
            Canvas.SetLeft(border, rect.X - 3);
            Canvas.SetTop(border, rect.Y - 3);
            border.MouseLeftButtonDown += (_, args) => { SelectSegment(segment); args.Handled = true; };
            border.MouseEnter += (_, _) => { if (_selected?.ObjectIndex != segment.ObjectIndex) border.BorderBrush = new SolidColorBrush(Color.FromRgb(123, 165, 154)); };
            border.MouseLeave += (_, _) => { if (_selected?.ObjectIndex != segment.ObjectIndex) border.BorderBrush = Brushes.Transparent; };
            SelectionCanvas.Children.Add(border);
        }
    }

    private void Segments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _busy || SegmentsList.SelectedItem is not TextSegment segment) return;
        SelectSegment(segment);
    }

    private void SelectSegment(TextSegment segment)
    {
        if (_busy || _preview != null || _selected?.ObjectIndex == segment.ObjectIndex) return;
        if (!DiscardDraft())
        {
            _updating = true;
            SegmentsList.SelectedItem = _selected;
            _updating = false;
            return;
        }
        _selected = segment;
        _updating = true;
        SegmentsList.SelectedItem = segment;
        OriginalText.Text = segment.Text;
        FontLabel.Text = $"{segment.FontFamily} · {segment.FontSize:0.#} pt{(segment.IsBold ? " · Bold" : "")}{(segment.IsItalic ? " · Italic" : "")}";
        ApplyInputTypography(segment);
        ReplacementBox.Text = segment.Text;
        _updating = false;
        SetMessage(segment.Restriction ?? "Edit the text, then preview. The original font and size will be preserved.", !segment.CanEdit);
        if (_view != null) DrawSelection(_view);
        UpdateControls();
    }

    private void ApplyInputTypography(TextSegment segment)
    {
        var family = Fonts.SystemFontFamilies.FirstOrDefault(f =>
            f.Source.Equals(segment.FontFamily, StringComparison.OrdinalIgnoreCase) ||
            f.FamilyNames.Values.Any(name => name.Equals(segment.FontFamily, StringComparison.OrdinalIgnoreCase)));
        // UI font availability does not alter the font embedded in the PDF.
        // The page preview remains authoritative when Windows lacks the face.
        var fontFamily = family ?? new FontFamily("Segoe UI");
        OriginalText.FontFamily = ReplacementBox.FontFamily = fontFamily;
        OriginalText.FontSize = ReplacementBox.FontSize = Math.Clamp(segment.FontSize * 96.0 / 72, 6, 96);
        OriginalText.FontWeight = ReplacementBox.FontWeight = segment.IsBold ? FontWeights.Bold : FontWeights.Normal;
        OriginalText.FontStyle = ReplacementBox.FontStyle = segment.IsItalic ? FontStyles.Italic : FontStyles.Normal;
        TypographyHint.Text = family != null
            ? "The selected text's font, size, and style are applied automatically."
            : "The PDF keeps its original font. Windows uses a fallback in this typing box; check the page preview.";
    }

    private void Replacement_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || PreviewButton == null) return;
        UpdateControls();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || _selected == null || !_selected.CanEdit || _preview != null) return;
        var replacement = ReplacementBox.Text;
        var objectIndex = _selected.ObjectIndex;
        await RunBusyAsync("Checking text, fonts, and every page…", async () =>
        {
            _preview = await Task.Run(() => _editor.Preview(_pageIndex, objectIndex, replacement, RenderScale));
            ShowPage(_preview.View);
            SetMessage("Validation passed. Review the preview, then choose Apply to keep this change.");
            StatusLabel.Text = "Preview ready. The change has not been applied.";
        });
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || _preview == null || _busy) return;
        try
        {
            _editor.Apply(_preview);
            _view = _preview.View;
            _preview = null;
            ResetSelection();
            ShowPage(_view);
            SetMessage("Change applied. You can undo it or save an edited copy.");
            StatusLabel.Text = "Text updated · unsaved changes";
        }
        catch (PdfEditException ex) { SetMessage(ex.Message, true); }
        UpdateControls();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _preview = null;
        _updating = true;
        ReplacementBox.Text = _selected?.Text ?? "";
        _updating = false;
        if (_view != null) ShowPage(_view);
        SetMessage("Preview cancelled. The document has not changed.");
        UpdateControls();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await NavigateAsync(_pageIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await NavigateAsync(_pageIndex + 1);
    private async Task NavigateAsync(int index)
    {
        if (_editor == null || _busy || index < 0 || index >= _editor.PageCount || !DiscardDraft()) return;
        await RunBusyAsync("Loading page…", async () =>
        {
            var view = await Task.Run(() => _editor.ReadPage(index, RenderScale));
            _pageIndex = index;
            _view = view;
            ResetSelection();
            ShowPage(view, true);
            StatusLabel.Text = $"Page {index + 1} of {_editor.PageCount}";
        });
    }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e) => await ZoomAsync(.15);
    private async void ZoomOut_Click(object sender, RoutedEventArgs e) => await ZoomAsync(-.15);
    private async Task ZoomAsync(double delta)
    {
        if (_editor == null || _busy || _preview != null) return;
        var zoom = Math.Clamp(Math.Round(_zoom + delta, 2), .4, 2.5);
        await RunBusyAsync("Rendering page…", async () =>
        {
            var view = await Task.Run(() => _editor.ReadPage(_pageIndex, zoom * 96 / 72));
            _zoom = zoom;
            _view = view;
            ShowPage(view);
        });
    }

    private async void Undo_Click(object sender, RoutedEventArgs e) => await HistoryAsync(false);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await HistoryAsync(true);
    private async Task HistoryAsync(bool redo)
    {
        if (_editor == null || _busy || !DiscardDraft()) return;
        await RunBusyAsync(redo ? "Restoring edit…" : "Undoing edit…", async () =>
        {
            _view = await Task.Run(() => { if (redo) _editor.Redo(); else _editor.Undo(); return _editor.ReadPage(_pageIndex, RenderScale); });
            ResetSelection();
            ShowPage(_view);
            StatusLabel.Text = redo ? "Edit restored." : "Edit undone.";
        });
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async Task<bool> SaveAsync()
    {
        if (_editor == null || _busy) return false;
        if (HasDraft)
        {
            MessageBox.Show(this, "Apply or cancel your text change before saving.", "Finish your edit", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "PDF documents (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_editor.SourcePath) + "-edited.pdf",
            Title = "Save an edited copy — choose a new filename", OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return false;
        return await SaveCopyAsync(dialog.FileName);
    }

    internal async Task<bool> SaveCopyAsync(string path)
    {
        if (_editor == null || _busy || HasDraft) return false;
        var saved = false;
        await RunBusyAsync("Saving your edited copy…", async () =>
        {
            await Task.Run(() => _editor.SaveCopy(path));
            StatusLabel.Text = "Saved: " + path;
            SetMessage("Your edited copy has been saved. The original file is intact.");
            saved = true;
        });
        return saved;
    }

    private bool HasDraft => _preview != null || (_selected?.CanEdit == true && ReplacementBox.Text != _selected.Text);
    private bool DiscardDraft()
    {
        if (HasDraft && MessageBox.Show(this, "Discard the text change you have not applied?", "Unapplied text change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        _preview = null;
        _updating = true;
        ReplacementBox.Text = _selected?.Text ?? "";
        _updating = false;
        if (_view != null) ShowPage(_view);
        return true;
    }
    private async Task<bool> MayLeaveDocumentAsync()
    {
        if (!DiscardDraft()) return false;
        if (_editor?.IsDirty != true) return true;
        var answer = MessageBox.Show(this, "Save an edited copy before leaving this document?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch { MessageBoxResult.Yes => await SaveAsync(), MessageBoxResult.No => true, _ => false };
    }

    private async Task RunBusyAsync(string message, Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        BusyLabel.Text = message;
        BusyOverlay.Visibility = Visibility.Visible;
        UpdateControls();
        try { await work(); }
        catch (Exception ex)
        {
            var detail = ex is PdfEditException or IOException or UnauthorizedAccessException ? ex.Message
                : ex is DllNotFoundException or TypeInitializationException ? "The PDF engine could not load. Extract the complete application folder and try again."
                : "The operation could not be completed. Your original file has not been overwritten.";
            SetMessage(detail, true);
            StatusLabel.Text = detail;
            if (_editor == null || message.StartsWith("Saving", StringComparison.Ordinal)) MessageBox.Show(this, detail, "DirectPDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { _busy = false; BusyOverlay.Visibility = Visibility.Collapsed; UpdateControls(); }
    }

    private void ResetSelection()
    {
        _selected = null;
        _preview = null;
        _updating = true;
        SegmentsList.SelectedItem = null;
        OriginalText.Text = "No text selected";
        FontLabel.Text = "";
        OriginalText.FontFamily = ReplacementBox.FontFamily = FontFamily;
        OriginalText.FontSize = ReplacementBox.FontSize = FontSize;
        OriginalText.FontWeight = ReplacementBox.FontWeight = FontWeights.Normal;
        OriginalText.FontStyle = ReplacementBox.FontStyle = FontStyles.Normal;
        TypographyHint.Text = "The selected text's font, size, and style are applied automatically.";
        ReplacementBox.Text = "";
        _updating = false;
    }
    private void SetMessage(string text, bool error = false)
    {
        EditorMessage.Text = text;
        EditorMessage.Foreground = error ? new SolidColorBrush(Color.FromRgb(139, 71, 36)) : (Brush)FindResource("Muted");
        MessageBorder.Background = error ? new SolidColorBrush(Color.FromRgb(255, 244, 231)) : new SolidColorBrush(Color.FromRgb(240, 245, 244));
    }
    private void UpdateControls()
    {
        var ready = !_busy && _editor != null;
        OpenButton.IsEnabled = !_busy;
        SaveButton.IsEnabled = ready && !HasDraft;
        UndoButton.IsEnabled = ready && _editor!.CanUndo && _preview == null;
        RedoButton.IsEnabled = ready && _editor!.CanRedo && _preview == null;
        PreviousButton.IsEnabled = ready && _pageIndex > 0 && _preview == null;
        NextButton.IsEnabled = ready && _pageIndex + 1 < _editor!.PageCount && _preview == null;
        ZoomInButton.IsEnabled = ready && _zoom < 2.5 && _preview == null;
        ZoomOutButton.IsEnabled = ready && _zoom > .4 && _preview == null;
        SegmentsList.IsEnabled = ready && _preview == null;
        ReplacementBox.IsEnabled = ready && _selected?.CanEdit == true && _preview == null;
        PreviewButton.IsEnabled = ready && _selected?.CanEdit == true && ReplacementBox.Text != _selected.Text && _preview == null;
        ApplyButton.IsEnabled = ready && _preview != null;
        CancelButton.IsEnabled = ready && HasDraft;
        Title = _editor == null ? "DirectPDF" : $"{(_editor.IsDirty ? "* " : "")}{Path.GetFileName(_editor.SourcePath)} — DirectPDF";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await OpenPathAsync(files[0]);
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_busy) { e.Cancel = true; return; }
        e.Cancel = true;
        if (await MayLeaveDocumentAsync()) { _closing = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (e.Key == Key.O) { e.Handled = true; Open_Click(this, new RoutedEventArgs()); }
        else if (e.Key == Key.S) { e.Handled = true; await SaveAsync(); }
        else if (!ReplacementBox.IsKeyboardFocusWithin && e.Key == Key.Z) { e.Handled = true; await HistoryAsync(false); }
        else if (!ReplacementBox.IsKeyboardFocusWithin && e.Key == Key.Y) { e.Handled = true; await HistoryAsync(true); }
    }
}
