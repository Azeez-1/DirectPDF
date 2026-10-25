using DirectPDF.Core;
using DirectPDF.Tests;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "tests"));
Directory.CreateDirectory(root);
if (args.Contains("--demo"))
{
    File.WriteAllBytes(Path.Combine(root, "Welcome.pdf"), Fixtures.Demo());
    Console.WriteLine("Created Welcome.pdf");
    return 0;
}
var run = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var failures = new List<string>();
var passed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL {name}\n{ex}"); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action body, string message)
{
    try { body(); } catch (PdfEditException ex) { Check(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), $"Unexpected rejection: {ex.Message}"); return; }
    throw new Exception("Expected the operation to be rejected.");
}
PdfEditor Open(string name, byte[] data)
{
    var path = Path.Combine(run, name + ".pdf");
    File.WriteAllBytes(path, data);
    return PdfEditor.Open(path);
}

Test("Open and render a two-page PDF", () =>
{
    var editor = Open("open", Fixtures.Simple());
    var page = editor.ReadPage(0);
    Check(editor.PageCount == 2, "Page count");
    Check(page.Segments.Count == 2 && page.Segments.All(s => s.CanEdit), "Editable text detection");
    Check(page.Bitmap.Width == 918 && page.Bitmap.Height == 1188, "Rendered dimensions");
});
Test("Edit actual text, preserve surrounding pixels, save and reopen", () =>
{
    var source = Fixtures.Simple();
    var editor = Open("replace", source);
    var before = editor.ReadPage(1);
    var segment = editor.ReadPage(0).Segments[0];
    var preview = editor.Preview(0, segment.ObjectIndex, "Invoice 54321");
    Check(!editor.IsDirty, "A preview must not mutate the document");
    Check(editor.ReadPage(0).Segments[0].Text == segment.Text, "Original stays intact before apply");
    editor.Apply(preview);
    Check(editor.IsDirty && editor.CanUndo, "Applied edit must be undoable");
    var saved = Path.Combine(run, "edited.pdf");
    editor.SaveCopy(saved);
    var reopened = PdfEditor.Open(saved);
    Check(reopened.ReadPage(0).Segments[0].Text == "Invoice 54321", "Saved text must be searchable");
    Check(reopened.ReadPage(0).Segments[1].Text == "Keep this line unchanged.", "Other text unchanged");
    Check(before.Bitmap.Pixels.SequenceEqual(reopened.ReadPage(1).Bitmap.Pixels), "Other page pixels unchanged");
    Check(File.ReadAllBytes(editor.SourcePath).SequenceEqual(source), "Source file untouched");
    Check(!editor.IsDirty, "Save clears dirty state");
});
Test("Undo, redo, saved-state tracking, and stale preview", () =>
{
    var editor = Open("history", Fixtures.Simple());
    var segment = editor.ReadPage(0).Segments[0];
    var preview = editor.Preview(0, segment.ObjectIndex, "Invoice 54321");
    editor.Apply(preview);
    editor.Undo();
    Check(!editor.IsDirty && editor.CanRedo, "Undo must restore original state");
    Reject(() => editor.Apply(preview), "out of date");
    editor.Redo();
    Check(editor.ReadPage(0).Segments[0].Text == "Invoice 54321", "Redo text");
    editor.SaveCopy(Path.Combine(run, "history-copy.pdf"));
    editor.Undo(); Check(editor.IsDirty, "Undo after save is dirty");
    editor.Redo(); Check(!editor.IsDirty, "Redo to saved state is clean");
});
Test("Reject overflow without mutating history", () =>
{
    var editor = Open("overflow", Fixtures.Simple());
    var segment = editor.ReadPage(0).Segments[0];
    Reject(() => editor.Preview(0, segment.ObjectIndex, new string('W', 100)), "wider");
    Check(!editor.IsDirty && !editor.CanUndo, "Failed edits must not mutate state");
});
Test("Delete a text object without covering it", () =>
{
    var editor = Open("delete", Fixtures.Simple());
    var segment = editor.ReadPage(0).Segments[0];
    editor.Apply(editor.Preview(0, segment.ObjectIndex, ""));
    Check(editor.ReadPage(0).Segments.Count == 1, "Text object removed");
    Check(editor.ReadPage(0).Segments[0].Text == "Keep this line unchanged.", "Neighbour retained");
});
Test("Reject unsupported characters and multiline replacements", () =>
{
    var editor = Open("unicode", Fixtures.Simple());
    var index = editor.ReadPage(0).Segments[0].ObjectIndex;
    Reject(() => editor.Preview(0, index, "Hello\nworld"), "single line");
    Reject(() => editor.Preview(0, index, "Hello 😀"), "single line");
});
Test("Re-typeset selected custom-spaced text using its original font", () =>
{
    var editor = Open("kerned", Fixtures.Kerned());
    var segment = editor.ReadPage(0).Segments[0];
    editor.Apply(editor.Preview(0, segment.ObjectIndex, "Hi"));
    var changed = editor.ReadPage(0).Segments[0];
    Check(changed.Text == "Hi" && changed.Font == segment.Font && changed.FontSize == segment.FontSize, "Keep the original font and size");
});
Test("Longer replacements use empty space without shrinking the font", () =>
{
    var editor = Open("longer", Fixtures.Simple("TECHNICAL REPORT"));
    var segment = editor.ReadPage(0).Segments[0];
    editor.Apply(editor.Preview(0, segment.ObjectIndex, "TECHNICAL TESTING"));
    var changed = editor.ReadPage(0).Segments[0];
    Check(changed.Text == "TECHNICAL TESTING" && changed.Bounds.Width > segment.Bounds.Width, "Longer replacement expands");
    Check(changed.FontSize == segment.FontSize && changed.Font == segment.Font, "Font is retained automatically");
});
Test("Empty trailing text objects do not block visible text", () =>
{
    var editor = Open("empty-trailing", Fixtures.Simple("HI", extra: "BT /F1 18 Tf 1 0 0 1 82 710 Tm ( ) Tj ET"));
    editor.Apply(editor.Preview(0, editor.ReadPage(0).Segments[0].ObjectIndex, "HELLO"));
    Check(editor.ReadPage(0).Segments[0].Text == "HELLO", "Trailing empty object ignored");
});
Test("Longer replacements still cannot cover neighbouring text", () =>
{
    var editor = Open("collision", Fixtures.Simple("HI", extra: "BT /F1 18 Tf 1 0 0 1 90 710 Tm (NEIGHBOUR) Tj ET"));
    Reject(() => editor.Preview(0, editor.ReadPage(0).Segments[0].ObjectIndex, "HELLO THERE"), "overlap");
    Check(!editor.IsDirty, "Rejected collision leaves the original unchanged");
});
Test("Preserve untouched short words with character and word spacing", () =>
{
    var editor = Open("word-spacing", Fixtures.Simple(extra: "BT /F1 12 Tf -1.34 Tc 1 0 0 1 300 600 Tm (AT) Tj ET\nBT /F1 12 Tf 0 Tc 4 Tw 1 0 0 1 300 570 Tm (A B) Tj ET"));
    var original = editor.ReadPage(0);
    editor.Apply(editor.Preview(0, original.Segments[0].ObjectIndex, "Invoice 54321"));
    var after = editor.ReadPage(0);
    Check(after.Segments.Skip(1).Select(s => s.Text).SequenceEqual(original.Segments.Skip(1).Select(s => s.Text)), "Unedited text retained");
    // The core requires exact pixel equality outside the selected text; this
    // fixture fails without repairing Tc/Tw spacing after serialization.
    Check(after.Segments.Single(s => s.Text == "AT").Bounds == original.Segments.Single(s => s.Text == "AT").Bounds, "Short-word placement retained");
});
Test("Rotated pages are view-only and enforced in the core", () =>
{
    var editor = Open("rotated", Fixtures.Simple(pageOptions: "/Rotate 90"));
    var segment = editor.ReadPage(0).Segments[0];
    Check(!segment.CanEdit, "Rotated page restriction");
    Reject(() => editor.Preview(0, segment.ObjectIndex, "Test"), "Rotated");
});
Test("Scanned/outlined pages do not offer fake text editing", () =>
{
    var editor = Open("no-text", Fixtures.NoText());
    Check(editor.ReadPage(0).Segments.Count == 0, "No invented text objects");
});
Test("Interactive form documents are view-only", () =>
{
    var editor = Open("form", Fixtures.Simple(catalogOptions: "/AcroForm << /Fields [] >>"));
    Check(editor.ReadPage(0).Restriction != null, "Form restriction");
});
Test("Saving cannot overwrite source or existing files", () =>
{
    var editor = Open("save-safety", Fixtures.Simple());
    Reject(() => editor.SaveCopy(editor.SourcePath), "original");
    var existing = Path.Combine(run, "existing.pdf");
    File.WriteAllText(existing, "keep me");
    Reject(() => editor.SaveCopy(existing), "already exists");
    Check(File.ReadAllText(existing) == "keep me", "Existing file retained");
});
Test("Reject previews from another document", () =>
{
    var a = Open("document-a", Fixtures.Simple());
    var b = Open("document-b", Fixtures.Simple());
    var preview = a.Preview(0, a.ReadPage(0).Segments[0].ObjectIndex, "Invoice");
    Reject(() => b.Apply(preview), "out of date");
});
Test("Embedded CID TrueType font survives actual replacement", () =>
{
    var editor = Open("embedded", EmbeddedFixture.Create());
    var segment = editor.ReadPage(0).Segments[0];
    Check(segment.CanEdit, segment.Restriction ?? "Embedded font eligibility");
    editor.Apply(editor.Preview(0, segment.ObjectIndex, "Invoice 54321"));
    editor.SaveCopy(Path.Combine(run, "embedded-copy.pdf"));
    var reopened = PdfEditor.Open(Path.Combine(run, "embedded-copy.pdf"));
    Check(reopened.ReadPage(0).Segments[0].Text == "Invoice 54321", "Embedded font text survived save");
});
Test("Embedded fonts reject unverified characters", () =>
{
    var editor = Open("missing-glyph", EmbeddedFixture.Create());
    Reject(() => editor.Preview(0, editor.ReadPage(0).Segments[0].ObjectIndex, "@"), "no verified mapping");
});
Test("Clipped text is detected and blocked", () =>
{
    var editor = Open("clipped", Fixtures.Simple(extra: "q 60 580 50 30 re W n BT /F1 18 Tf 1 0 0 1 60 590 Tm (Clipped text example) Tj ET Q"));
    var segment = editor.ReadPage(0).Segments.Last();
    Check(!segment.CanEdit, "Clipped segment cannot edit");
    Reject(() => editor.Preview(0, segment.ObjectIndex, "Text"), "clipping");
});
Test("Sample document can complete the advertised edit", () =>
{
    var editor = Open("demo", Fixtures.Demo());
    var segment = editor.ReadPage(0).Segments.Single(s => s.Text == "Status: Waiting for final review");
    editor.Apply(editor.Preview(0, segment.ObjectIndex, "Status: Ready"));
    Check(editor.ReadPage(0).Segments.Any(s => s.Text == "Status: Ready"), "Sample edit");
});
Test("Repeated edits validate and save correctly", () =>
{
    var editor = Open("repeat", Fixtures.Simple());
    var first = editor.ReadPage(0).Segments[0];
    editor.Apply(editor.Preview(0, first.ObjectIndex, "Invoice 54321"));
    var second = editor.ReadPage(0).Segments[0];
    editor.Apply(editor.Preview(0, second.ObjectIndex, "Invoice 123"));
    editor.Undo();
    Check(editor.ReadPage(0).Segments[0].Text == "Invoice 54321", "Repeated-edit undo");
    editor.Redo();
    Check(editor.ReadPage(0).Segments[0].Text == "Invoice 123", "Repeated-edit redo");
});
Test("Reject corrupt input cleanly", () =>
{
    Reject(() => Open("invalid", "not a pdf"u8.ToArray()), "could not be opened");
});
Console.WriteLine($"\n{passed} passed, {failures.Count} failed. Artifacts: {run}");
return failures.Count == 0 ? 0 : 1;
