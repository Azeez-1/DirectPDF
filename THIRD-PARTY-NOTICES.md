# Third-party notices

DirectPDF's own source is MIT licensed. Its dependencies retain their own licenses.

| Component | Version | License / source |
| --- | --- | --- |
| PDFium Windows binaries, packaged by Benoît Blanchon | bblanchon.PDFium.Win32 158.0.8111 | Packaging: Apache-2.0; https://github.com/bblanchon/pdfium-binaries |
| PDFium and bundled native dependencies | Chromium build 8111 | PDFium BSD-style license and individual dependency licenses; see `licenses/pdfium/` |
| .NET / WPF | .NET 10 | MIT and third-party terms; https://github.com/dotnet/runtime and https://github.com/dotnet/wpf |

The native package is pinned and recorded in NuGet lock files. The license texts
for the matching PDFium binary release are included in `licenses/pdfium/`.
The optional self-contained distribution also includes the .NET runtime's
license and third-party notices supplied by its runtime packages.

The sample PDF is authored for this project. Integration tests may use an installed
Windows font locally; that font and PDFs containing it are not distributed in
the repository or application package.
