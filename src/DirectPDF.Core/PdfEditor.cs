using System.Text;

namespace DirectPDF.Core;

/// <summary>Transactional PDF editing. Native handles never escape this class.</summary>
public sealed class PdfEditor
{
    private const int MaxFileBytes = 100 * 1024 * 1024;
    private const int MaxHistoryBytes = 128 * 1024 * 1024;
    private readonly Guid _id = Guid.NewGuid();
    private readonly List<byte[]> _undo = [];
    private readonly List<byte[]> _redo = [];
    private byte[] _current;
    private byte[] _saved;
    private long _version;
    public string SourcePath { get; }
    public int PageCount { get; }
    public bool CanUndo { get { lock (Native.Gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (Native.Gate) return _redo.Count > 0; } }
    public bool IsDirty { get { lock (Native.Gate) return !_current.AsSpan().SequenceEqual(_saved); } }

    private PdfEditor(string source, byte[] bytes)
    {
        SourcePath = source;
        _current = _saved = bytes;
        using var doc = new PdfDocument(bytes);
        PageCount = doc.PageCount;
        if (PageCount == 0) throw new PdfEditException("This PDF has no pages.");
    }

    public static PdfEditor Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (new FileInfo(fullPath).Length > MaxFileBytes)
            throw new PdfEditException("This first version supports PDFs up to 100 MB.");
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length > MaxFileBytes) throw new PdfEditException("This PDF exceeds the 100 MB limit.");
        lock (Native.Gate) return new PdfEditor(fullPath, bytes);
    }

    public PageView ReadPage(int index, double scale = 1.5)
    {
        lock (Native.Gate) return ReadPage(_current, index, scale);
    }

    public EditPreview Preview(int pageIndex, int objectIndex, string replacement, double scale = 1.5)
    {
        lock (Native.Gate)
        {
            replacement = NormalizeSpaces(replacement);
            if (replacement.Length > 2000) throw new PdfEditException("A text segment can contain at most 2,000 characters.");
            if (replacement.Any(c => !IsSupportedCharacter(c)))
                throw new PdfEditException("Use a single line of English or supported Latin text. Line breaks, emoji, and complex scripts are not supported yet.");
            if (replacement.Length > 0 && string.IsNullOrWhiteSpace(replacement))
                throw new PdfEditException("To remove the selected text, leave the replacement completely empty.");

            using var original = new PdfDocument(_current);
            using var originalPage = original.Page(pageIndex);
            var segment = GetSegments(originalPage, original.EditingRestriction, 1, 1)
                .FirstOrDefault(s => s.ObjectIndex == objectIndex)
                ?? throw new PdfEditException("Select a text segment before editing.");
            if (!segment.CanEdit) throw new PdfEditException(segment.Restriction!);
            if (replacement == segment.Text) throw new PdfEditException("Change the text before creating a preview.");

            var originalObj = Native.FPDFPage_GetObject(originalPage.Handle, objectIndex);
            ValidateCharacters(original, originalObj, replacement);

            // SetText retains the selected object's font, size, fill and matrix.
            // Re-typeset this selected run using that font's advances. Word PDFs
            // frequently contain per-letter positioning: requiring a pixel-exact
            // no-op replacement rejects valid edits before they can be previewed.
            // Validate the actual saved result and all surrounding content below.
            byte[] candidateBytes;
            var changedBounds = segment.Bounds;
            using (var candidate = new PdfDocument(_current))
            {
                using (var page = candidate.Page(pageIndex))
                {
                    SetText(page, objectIndex, replacement);
                    if (replacement.Length > 0)
                    {
                        var obj = Native.FPDFPage_GetObject(page.Handle, objectIndex);
                        var bounds = Bounds(obj);
                        if (bounds.Left < segment.Bounds.Left - segment.FontSize * .3)
                            throw new PdfEditException("The replacement would extend too far left of the original text. Shorten it to preserve the layout.");
                        changedBounds = segment.Bounds.Union(bounds);
                        var pixels = page.ToPixels(bounds, (int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height));
                        if (pixels.X < 0 || pixels.Y < 0 || pixels.X + pixels.Width > Math.Ceiling(page.Width) || pixels.Y + pixels.Height > Math.Ceiling(page.Height))
                            throw new PdfEditException("The replacement is wider than the available space on the page. Shorten it to fit.");
                        CheckCollisions(originalPage, objectIndex, segment.Bounds, bounds);
                    }
                }
                candidateBytes = candidate.Save();
            }

            // The first serialization normalizes PDF text-state operators. Repair
            // any untouched glyph advances in that normalized document, where
            // they can be represented explicitly and saved without losing Tc/Tw.
            using (var normalized = new PdfDocument(candidateBytes))
            {
                using (var page = normalized.Page(pageIndex))
                {
                    ValidateSegments(originalPage, page, objectIndex, replacement);
                    if (RestoreOtherTextPositions(originalPage, page, objectIndex, replacement.Length == 0))
                    {
                        if (Native.FPDFPage_GenerateContent(page.Handle) == 0)
                            throw new PdfEditException("The original text spacing could not be preserved.");
                        candidateBytes = normalized.Save();
                    }
                }
            }

            // Reopen the actual bytes that will be saved, then check every page.
            // Only the selected segment may change text or rendered pixels.
            using (var reopened = new PdfDocument(candidateBytes))
            {
                if (reopened.PageCount != original.PageCount) throw new PdfEditException("Page count changed during validation. The edit was blocked.");
                for (var i = 0; i < original.PageCount; i++)
                {
                    using var before = original.Page(i);
                    using var after = reopened.Page(i);
                    if (i == pageIndex)
                    {
                        ValidateSegments(before, after, objectIndex, replacement);
                        var beforeImage = before.Render(2);
                        var afterImage = after.Render(2);
                        var allowed = before.ToPixels(changedBounds, beforeImage.Width, beforeImage.Height);
                        if (!SamePixelsOutside(beforeImage, afterImage, allowed))
                            throw new PdfEditException("The edit changed content outside the selected text. It was blocked to preserve the page.");
                    }
                    else if (before.Text() != after.Text() || !SamePixels(before.Render(1), after.Render(1)))
                        throw new PdfEditException($"The edit unexpectedly changed page {i + 1}. It was blocked.");
                }
            }
            if (candidateBytes.Length > MaxFileBytes) throw new PdfEditException("The edited document exceeds the 100 MB limit.");
            return new EditPreview(candidateBytes, _version, _id, ReadPage(candidateBytes, pageIndex, scale));
        }
    }

    public void Apply(EditPreview preview)
    {
        lock (Native.Gate)
        {
            if (preview.Owner != _id || preview.Version != _version)
                throw new PdfEditException("This preview is out of date. Preview the change again.");
            _undo.Add(_current);
            _redo.Clear();
            _current = preview.Bytes;
            _version++;
            while (_undo.Count > 1 && (_undo.Count > 20 || _undo.Sum(b => (long)b.Length) > MaxHistoryBytes)) _undo.RemoveAt(0);
        }
    }

    public void Undo() { lock (Native.Gate) MoveHistory(_undo, _redo); }
    public void Redo() { lock (Native.Gate) MoveHistory(_redo, _undo); }
    private void MoveHistory(List<byte[]> from, List<byte[]> to)
    {
        if (from.Count == 0) return;
        to.Add(_current);
        _current = from[^1];
        from.RemoveAt(from.Count - 1);
        _version++;
    }

    public void SaveCopy(string path)
    {
        lock (Native.Gate)
        {
            var destination = Path.GetFullPath(path);
            if (string.Equals(destination, SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new PdfEditException("Choose a different filename to keep the original PDF intact.");
            // A new filename also avoids overwriting through hard links, aliases,
            // or another program changing the destination after the file dialog.
            if (File.Exists(destination)) throw new PdfEditException("That file already exists. Choose a new filename for the edited copy.");
            var directory = Path.GetDirectoryName(destination)!;
            var temporary = Path.Combine(directory, $".directpdf-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(_current);
                    file.Flush(true);
                }
                File.Move(temporary, destination, false);
                _saved = _current;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static PageView ReadPage(byte[] bytes, int index, double scale)
    {
        using var doc = new PdfDocument(bytes);
        using var page = doc.Page(index);
        var bitmap = page.Render(scale);
        var restriction = doc.EditingRestriction;
        if (Native.FPDFPage_GetRotation(page.Handle) != 0) restriction ??= "Rotated pages are view-only in this version.";
        return new PageView(index, doc.PageCount, bitmap, GetSegments(page, restriction, bitmap.Width, bitmap.Height), restriction);
    }

    private static List<TextSegment> GetSegments(PdfPage page, string? documentRestriction, int width, int height)
    {
        var result = new List<TextSegment>();
        var textPage = Native.FPDFText_LoadPage(page.Handle);
        if (textPage == IntPtr.Zero) throw new PdfEditException("The page text could not be inspected.");
        try
        {
            var badMappings = new HashSet<IntPtr>();
            for (var c = 0; c < Native.FPDFText_CountChars(textPage); c++)
                if (Native.FPDFText_HasUnicodeMapError(textPage, c) != 0) badMappings.Add(Native.FPDFText_GetTextObject(textPage, c));
            for (var i = 0; i < Native.FPDFPage_CountObjects(page.Handle); i++)
            {
                var obj = Native.FPDFPage_GetObject(page.Handle, i);
                if (Native.FPDFPageObj_GetType(obj) != 1) continue;
                var text = ObjectText(obj, textPage);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var font = Native.FPDFTextObj_GetFont(obj);
                var fontName = FontName(font);
                Native.FPDFTextObj_GetFontSize(obj, out var size);
                var bounds = Bounds(obj);
                string? restriction = documentRestriction;
                if (Native.FPDFPage_GetRotation(page.Handle) != 0) restriction ??= "Rotated pages are view-only in this version.";
                if (text.Any(c => !IsSupportedCharacter(c)) || badMappings.Contains(obj)) restriction ??= "This text uses an unsupported script or character mapping.";
                if (Native.FPDFPageObj_GetMatrix(obj, out var matrix) == 0 || !float.IsFinite(matrix.A) || !float.IsFinite(matrix.D) ||
                    Math.Abs(matrix.B) > .0001 || Math.Abs(matrix.C) > .0001 || matrix.A <= 0 || matrix.D <= 0)
                    restriction ??= "Rotated, skewed, and mirrored text is view-only.";
                if (Native.FPDFTextObj_GetTextRenderMode(obj) != 0 || Native.FPDFPageObj_HasTransparency(obj) != 0 ||
                    Native.FPDFPageObj_GetFillColor(obj, out _, out _, out _, out var alpha) == 0 || alpha != 255)
                    restriction ??= "Text with outlines, transparency, or clipping is view-only.";
                var clip = Native.FPDFPageObj_GetClipPath(obj);
                if (clip != IntPtr.Zero && Native.FPDFClipPath_CountPaths(clip) > 0)
                    restriction ??= "Text inside a clipping path is view-only in this version.";
                if (font == IntPtr.Zero || size <= 0 || !float.IsFinite(size) || bounds.Width <= 0 || bounds.Height <= 0)
                    restriction ??= "This text's font or dimensions could not be read safely.";
                if (Native.FPDFFont_GetIsEmbedded(font) != 1 && !StandardFonts.Contains(fontName))
                    restriction ??= "The original font is not embedded. This version edits embedded fonts and standard PDF fonts.";
                var family = FontFamilyName(font);
                var weight = Native.FPDFFont_GetWeight(font);
                Native.FPDFFont_GetItalicAngle(font, out var angle);
                result.Add(new TextSegment(i, text, fontName, size, bounds, page.ToPixels(bounds, width, height), restriction)
                {
                    FontFamily = string.IsNullOrWhiteSpace(family) ? FontDisplayName(fontName) : family,
                    IsBold = weight >= 600 || fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase),
                    IsItalic = Math.Abs(angle) > .1 || fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        finally { Native.FPDFText_ClosePage(textPage); }
        return result;
    }

    private static readonly HashSet<string> StandardFonts = new(StringComparer.Ordinal)
    {
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique"
    };
    private static bool IsSupportedCharacter(char c) => c is >= ' ' and <= '~' or >= '\u00a0' and <= '\u024f';

    private static void ValidateCharacters(PdfDocument doc, IntPtr obj, string replacement)
    {
        var font = Native.FPDFTextObj_GetFont(obj);
        if (Native.FPDFFont_GetIsEmbedded(font) == 0 && StandardFonts.Contains(FontName(font)) && replacement.All(c => c is >= ' ' and <= '~')) return;
        // For embedded/subset fonts use only characters already represented in
        // this exact font resource. A matching family name is not sufficient.
        var available = new HashSet<char>();
        for (var i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.Page(i);
            var textPage = Native.FPDFText_LoadPage(page.Handle);
            if (textPage == IntPtr.Zero) continue;
            try
            {
                for (var j = 0; j < Native.FPDFPage_CountObjects(page.Handle); j++)
                {
                    var other = Native.FPDFPage_GetObject(page.Handle, j);
                    if (Native.FPDFPageObj_GetType(other) == 1 && Native.FPDFTextObj_GetFont(other) == font)
                        available.UnionWith(ObjectText(other, textPage));
                }
            }
            finally { Native.FPDFText_ClosePage(textPage); }
        }
        var missing = replacement.Where(c => !available.Contains(c)).Distinct().ToArray();
        if (missing.Length > 0) throw new PdfEditException($"The original font has no verified mapping for: {new string(missing)}. Use characters already present in that font. Font substitution is not performed.");
    }

    private static void SetText(PdfPage page, int index, string text)
    {
        var obj = Native.FPDFPage_GetObject(page.Handle, index);
        if (text.Length == 0)
        {
            if (Native.FPDFPage_RemoveObject(page.Handle, obj) == 0) throw new PdfEditException("The selected text could not be removed.");
            Native.FPDFPageObj_Destroy(obj);
        }
        else if (Native.FPDFText_SetText(obj, text) == 0) throw new PdfEditException("The original font could not encode the replacement text.");
        if (Native.FPDFPage_GenerateContent(page.Handle) == 0) throw new PdfEditException("The page could not be regenerated safely.");
    }

    private static bool RestoreOtherTextPositions(PdfPage source, PdfPage target, int changedIndex, bool removed)
    {
        var origins = ReadTextOrigins(source);
        var currentOrigins = ReadTextOrigins(target);
        var changed = false;
        for (var i = 0; i < Native.FPDFPage_CountObjects(source.Handle); i++)
        {
            if (i == changedIndex) continue;
            var originalObject = Native.FPDFPage_GetObject(source.Handle, i);
            var obj = Native.FPDFPage_GetObject(target.Handle, removed && i > changedIndex ? i - 1 : i);
            if (!origins.TryGetValue(originalObject, out var points) || points.Count < 2 ||
                !currentOrigins.TryGetValue(obj, out var actual) || points.Count != actual.Count) continue;
            if (points.Zip(actual).All(pair => Math.Abs(pair.First.X - pair.Second.X) < .00001 && Math.Abs(pair.First.Y - pair.Second.Y) < .00001)) continue;
            if (Native.FPDFPageObj_GetMatrix(originalObject, out var m) == 0 || m.A <= 0 || m.D <= 0 || Math.Abs(m.B) > .0001 || Math.Abs(m.C) > .0001) continue;
            if (points.Any(p => !double.IsFinite(p.X) || Math.Abs(p.Y - points[0].Y) > .001)) continue;
            var positions = points.Skip(1).Select(p => (float)((p.X - points[0].X) / m.A)).ToArray();
            // Ligatures can expand into multiple extracted characters. When the
            // native glyph count differs, SetPositions rejects without mutation;
            // the final pixel comparison still protects these unsupported runs.
            if (Native.FPDFText_SetPositions(obj, positions, (nuint)positions.Length) != 0) changed = true;
        }
        return changed;
    }

    private static Dictionary<IntPtr, List<(double X, double Y)>> ReadTextOrigins(PdfPage page)
    {
        var textPage = Native.FPDFText_LoadPage(page.Handle);
        if (textPage == IntPtr.Zero) throw new PdfEditException("The page's original text spacing could not be read.");
        var origins = new Dictionary<IntPtr, List<(double X, double Y)>>();
        try
        {
            for (var i = 0; i < Native.FPDFText_CountChars(textPage); i++)
            {
                if (Native.FPDFText_IsGenerated(textPage, i) != 0) continue;
                var owner = Native.FPDFText_GetTextObject(textPage, i);
                if (owner == IntPtr.Zero || Native.FPDFText_GetCharOrigin(textPage, i, out var x, out var y) == 0) continue;
                if (!origins.TryGetValue(owner, out var points)) origins[owner] = points = [];
                points.Add((x, y));
            }
        }
        finally { Native.FPDFText_ClosePage(textPage); }
        return origins;
    }

    private static void CheckCollisions(PdfPage page, int index, PdfRect before, PdfRect after)
    {
        for (var j = 0; j < Native.FPDFPage_CountObjects(page.Handle); j++)
        {
            if (j == index) continue;
            var obj = Native.FPDFPage_GetObject(page.Handle, j);
            var other = Bounds(obj);
            // Word often emits empty trailing text objects at the end of a
            // heading. Their zero-area bounds are not visible obstacles.
            if (Native.FPDFPageObj_GetType(obj) == 1 && (other.Width <= 0 || other.Height <= 0)) continue;
            if (after.Intersects(other) && !before.Intersects(other))
                throw new PdfEditException("The replacement would overlap nearby content. Use shorter text.");
        }
    }

    private static void ValidateSegments(PdfPage before, PdfPage after, int changedIndex, string replacement)
    {
        var oldCount = Native.FPDFPage_CountObjects(before.Handle);
        var newCount = Native.FPDFPage_CountObjects(after.Handle);
        var removed = replacement.Length == 0;
        if (newCount != oldCount - (removed ? 1 : 0)) throw new PdfEditException("The page structure changed unexpectedly. The edit was blocked.");
        var oldText = Native.FPDFText_LoadPage(before.Handle);
        var newText = Native.FPDFText_LoadPage(after.Handle);
        try
        {
            for (var i = 0; i < oldCount; i++)
            {
                if (removed && i == changedIndex) continue;
                var oldObj = Native.FPDFPage_GetObject(before.Handle, i);
                var newObj = Native.FPDFPage_GetObject(after.Handle, removed && i > changedIndex ? i - 1 : i);
                var type = Native.FPDFPageObj_GetType(oldObj);
                if (type != Native.FPDFPageObj_GetType(newObj)) throw new PdfEditException("The page objects changed unexpectedly.");
                if (type != 1) continue;
                var expected = i == changedIndex ? replacement : ObjectText(oldObj, oldText);
                if (ObjectText(newObj, newText) != expected)
                    throw new PdfEditException("The saved text did not match the intended edit. The original font may use an unsupported mapping.");
                if (FontName(Native.FPDFTextObj_GetFont(oldObj)) != FontName(Native.FPDFTextObj_GetFont(newObj)))
                    throw new PdfEditException("The saved font changed unexpectedly.");
                ValidateTextStyle(oldObj, newObj);
            }
        }
        finally { Native.FPDFText_ClosePage(oldText); Native.FPDFText_ClosePage(newText); }
    }

    internal static string ObjectText(IntPtr obj, IntPtr textPage)
    {
        var length = Native.FPDFTextObj_GetText(obj, textPage, null, 0);
        if (length < 2) return "";
        var buffer = new byte[length];
        Native.FPDFTextObj_GetText(obj, textPage, buffer, length);
        return NormalizeSpaces(Encoding.Unicode.GetString(buffer, 0, checked((int)length - 2)));
    }
    // Some CID fonts map the shared space glyph to U+00A0 in ToUnicode.
    // Treat that equivalent extraction as an ordinary space in the editor;
    // saved text and surrounding-content validation still have to pass.
    private static string NormalizeSpaces(string text) => text.Replace('\u00a0', ' ');
    private static void ValidateTextStyle(IntPtr before, IntPtr after)
    {
        Native.FPDFTextObj_GetFontSize(before, out var beforeSize);
        Native.FPDFTextObj_GetFontSize(after, out var afterSize);
        Native.FPDFPageObj_GetMatrix(before, out var a);
        Native.FPDFPageObj_GetMatrix(after, out var b);
        Native.FPDFPageObj_GetFillColor(before, out var ar, out var ag, out var ab, out var aa);
        Native.FPDFPageObj_GetFillColor(after, out var br, out var bg, out var bb, out var ba);
        if (Math.Abs(beforeSize - afterSize) > .0001 ||
            Math.Abs(a.A - b.A) > .0001 || Math.Abs(a.B - b.B) > .0001 ||
            Math.Abs(a.C - b.C) > .0001 || Math.Abs(a.D - b.D) > .0001 ||
            Math.Abs(a.E - b.E) > .001 || Math.Abs(a.F - b.F) > .001 ||
            ar != br || ag != bg || ab != bb || aa != ba ||
            Native.FPDFTextObj_GetTextRenderMode(before) != Native.FPDFTextObj_GetTextRenderMode(after))
            throw new PdfEditException("The text's font size, style, colour, or position changed unexpectedly. The edit was blocked.");
    }

    private static string FontFamilyName(IntPtr font)
    {
        if (font == IntPtr.Zero) return "";
        var length = Native.FPDFFont_GetFamilyName(font, null, 0);
        if (length == 0) return "";
        var buffer = new byte[checked((int)length)];
        Native.FPDFFont_GetFamilyName(font, buffer, length);
        return Encoding.UTF8.GetString(buffer).TrimEnd('\0');
    }

    private static string FontDisplayName(string name) => name.Length > 7 && name[6] == '+' && name.Take(6).All(c => c is >= 'A' and <= 'Z') ? name[7..] : name;
    private static string FontName(IntPtr font)
    {
        if (font == IntPtr.Zero) return "Unknown font";
        var size = Native.FPDFFont_GetBaseFontName(font, null, 0);
        if (size == 0) return "Unknown font";
        var buffer = new byte[checked((int)size)];
        Native.FPDFFont_GetBaseFontName(font, buffer, size);
        return Encoding.UTF8.GetString(buffer).TrimEnd('\0');
    }
    private static PdfRect Bounds(IntPtr obj)
    {
        if (Native.FPDFPageObj_GetBounds(obj, out var l, out var b, out var r, out var t) == 0)
            throw new PdfEditException("The bounds of a page object could not be read safely.");
        return new PdfRect(l, b, r, t);
    }
    private static bool SamePixels(PageBitmap a, PageBitmap b) => a.Width == b.Width && a.Height == b.Height && a.Stride == b.Stride && a.Pixels.AsSpan().SequenceEqual(b.Pixels);
    private static bool SamePixelsOutside(PageBitmap a, PageBitmap b, PixelRect allowed)
    {
        if (a.Width != b.Width || a.Height != b.Height || a.Stride != b.Stride) return false;
        const int pad = 3; // anti-aliased edge pixels
        for (var y = 0; y < a.Height; y++)
        {
            var start = y * a.Stride;
            if (y < allowed.Y - pad || y > allowed.Y + allowed.Height + pad)
            {
                if (!a.Pixels.AsSpan(start, a.Width * 4).SequenceEqual(b.Pixels.AsSpan(start, b.Width * 4))) return false;
                continue;
            }
            var left = Math.Clamp(allowed.X - pad, 0, a.Width) * 4;
            var right = Math.Clamp(allowed.X + allowed.Width + pad + 1, 0, a.Width) * 4;
            if (!a.Pixels.AsSpan(start, left).SequenceEqual(b.Pixels.AsSpan(start, left)) ||
                !a.Pixels.AsSpan(start + right, a.Width * 4 - right).SequenceEqual(b.Pixels.AsSpan(start + right, a.Width * 4 - right))) return false;
        }
        return true;
    }
}
