# DirectPDF

A Windows desktop app for editing existing PDF text directly. No conversion,
account, upload, or network connection is required to edit a document.

**Status: 0.1.1 developer preview.** This is a working first implementation with a
deliberately narrow editing scope. It is not yet a general-purpose PDF editor.

## Try it

**Clone this repository, then double-click `Start DirectPDF.cmd`.** The Windows
x64 app and its .NET runtime are already included in `app/win-x64`. You do not
need to install .NET, an SDK, or download any additional dependencies to run it.
You can also open `app/win-x64/DirectPDF.exe` directly.

Windows 10/11, 64-bit, is the supported platform. Cloning downloads the bundled
application along with the source (about 147 MB uncompressed for the app).
After that, opening and editing PDFs works offline. Cloning itself does not
automatically execute the app. Keep the complete `app/win-x64` folder together.

Select **Try the sample**, click the status line, and replace it
with `Status: Ready`. Choose **Preview change**, inspect the page, **Apply**, and
**Save a copy**. Saving requires a new filename and preserves the original.

ARM64 and 32-bit Windows, macOS, and Linux are not supported by this bundle.

## Supported editing

- Existing, top-level horizontal text segments in digitally generated PDFs.
- English / simple Latin text, one PDF text object at a time.
- Original font, size, colour, and baseline retained.
- The typing box automatically uses the selected text's installed font, size,
  bold weight, and italic style. If that font is unavailable in Windows, the UI
  explains that the typing display uses a fallback; the PDF still uses its own font.
- Standard PDF Latin fonts, or embedded fonts using characters already present
  in the same font resource. A font-family name alone is insufficient.
- Replacement text that fits the available horizontal space, or complete deletion
  of the selected text object.
- Longer replacements can extend into empty space without shrinking the font.
  The app blocks overlaps with neighbouring content and text outside the page.
- Preview before apply, undo/redo, page navigation, zoom, drag-and-drop opening.
- Actual PDF text replacement: the old text is not covered with a rectangle.

Some visually continuous lines consist of multiple PDF text objects. These are
edited separately. Some visible text cannot be selected because it is inside a
Form XObject, outlined as vectors, or part of an image.

## Explicit limits

- No OCR, scanned-text editing, paragraph reflow, image editing, or font substitution.
- No editing of rotated/skewed/mirrored text, complex scripts, clipping paths,
  or transparent/stroked text. The selected run is re-typeset using its original
  font's advances; its manual letter spacing may change within the edited area.
  Untouched text must retain its rendering for the edit to be accepted.
- Rotated pages, encrypted documents, digitally signed PDFs, and documents with
  interactive forms are view-only. Files that need a password cannot yet open.
- 100 MB input/output limit. Render buffers are limited to 16 million pixels.
- Undo history keeps up to 20 edits, trimming older snapshots above 128 MB.
- The file is fully rewritten on an edit. This is **not a secure redaction tool**:
  metadata, attachments, structure information, or duplicate content may contain
  other copies of information.
- PDF/A conformance, tagged-PDF accessibility, and every third-party viewer have
  not been validated. Preview checks are evidence of fidelity, not a universal
  compatibility guarantee.
- Native parsing currently runs in the application process. An isolated PDF
  worker and a broader real-world compatibility corpus are future hardening work.

## Offline and online

| Feature | Availability |
| --- | --- |
| Open, render, select text, preview, apply, undo/redo, save | Offline |
| Obtaining the repository / updates | Internet required |
| Rebuilding from source (developers only) | Initial dependency download required |
| In-app update checking | Not implemented |
| OCR / cloud processing | Not implemented |

The app has no HTTP client, telemetry, account system, or document upload code.
Updates are manual in this preview.

## Build and validate

These steps are for developers changing the source. App users can use the
included launcher without building anything.

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on
Windows x64, then run from the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test-ui.ps1
.\scripts\run.ps1
.\scripts\publish.ps1
```

Use `scripts/publish.ps1 -SelfContained` to include the desktop runtime, then
`scripts/test-package.ps1` to extract and smoke-test that standalone ZIP.
To keep a running older app intact, publish to a different folder with
`-OutputDirectory artifacts/DirectPDF-0.1.1-win-x64-self-contained`.

Scripts also detect a project-local SDK at `.tools/dotnet/dotnet.exe`. Build caches
stay in ignored project folders. First restore needs NuGet network access; later
builds use the cache. Package versions and content hashes are locked.

If Windows blocks unsigned local PowerShell scripts, you can run a script with
a process-only exception, for example
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1`.
This does not change the machine's execution policy.

Equivalent commands with a globally installed SDK:

```powershell
dotnet restore DirectPDF.slnx --locked-mode
dotnet build DirectPDF.slnx -c Release --no-restore
dotnet run --project tests/DirectPDF.Tests -c Release --no-build
dotnet run --project src/DirectPDF.App -c Release --no-build
```

The test project is a console integration runner, not an xUnit project; use
`dotnet run`, **not** `dotnet test`, to execute it. It returns a nonzero exit code
on failure. Generated inputs and outputs stay under `artifacts/tests/`.
The separate UI runner exercises the WPF controls in an off-screen window and
saves screenshots plus its edited PDF under `artifacts/ui-tests/`.

The bundled app can be checked without an SDK using
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-portable.ps1`.
This verifies every bundled file, then opens the included PDF with the bundled
runtime. To refresh the committed bundle after a source change, publish a new
self-contained build and run `scripts/update-portable.ps1 -PackageDirectory`
with the distribution folder. Commit the refreshed bundle and manifest together.

## How validation works

1. Inspect eligibility and verify characters against the existing font resource.
2. Re-typeset the selected run in a disposable document using its original font,
   size, colour, and baseline. It may expand into empty space; new collisions
   with visible neighbouring content or the page edge are blocked.
   Space glyphs extracted as U+00A0 are normalized to ordinary spaces for editing
   and text comparison; the rendering checks still have to pass.
3. Save/reopen and restore any untouched glyph positions affected by PDFium's
   normalization of character/word-spacing operators. These are stored as explicit
   positions in the normalized document. Empty trailing text objects do not count
   as visible obstacles.
4. Save/reopen the result. Verify text, font names, sizes, matrices, and colours. Compare
   pixels outside the edited bounds on the changed page (up to 144 DPI) and
   extracted text plus rendering on every other page (up to 72 DPI).
5. Allow apply only after validation. Saving writes the already validated bytes
   to a temporary file, flushes, then moves to a new destination filename.

All native operations are serialized because PDFium is not thread-safe. UI work
awaits background operations. Large documents may take time because validation
checks every page. Snapshot-based undo avoids accumulating lossy reverse edits.

## Layout

```text
Start DirectPDF.cmd   Launch the included app without installing dependencies
START-HERE.txt        Instructions for app users
app/win-x64          Ready-to-run Windows app, native engine, runtime, and licenses
app/manifest.json    Version and SHA-256 hashes for the bundled files
src/DirectPDF.Core     PDFium bindings, editing, validation, history, safe save
src/DirectPDF.App      WPF desktop interface
tests/DirectPDF.Tests  Executable native integration tests and synthetic fixtures
samples/Welcome.pdf   Redistributable sample document
licenses/             Dependency license texts
scripts/              Build, test, run, and packaging helpers
```

The repository includes source and a runnable Windows bundle. GitHub Actions
checks the bundle, builds the source, and runs integration tests on Windows when
pushed. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT for DirectPDF code. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for
PDFium, binary packaging, and runtime licenses.
