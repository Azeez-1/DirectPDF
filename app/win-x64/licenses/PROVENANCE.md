# Native dependency license provenance

- NuGet package: `bblanchon.PDFium.Win32` version `158.0.8111`.
- Package source commit: `5b4d6b2ede1c1b0e17f5dc7b996c42d68d534ca8`.
- Corresponding release: https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium%2F8111
- License archive: https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8111/pdfium-win-x64.tgz
- Archive SHA-256: `3452D11031050EC65E57F180215B57B9E6D465E3FBEDAFA8A77AD90885C7D23E`.
- `pdfium/LICENSE`, `pdfium/VERSION`, and `pdfium/licenses/*` are extracted
  unchanged from that release archive.

The NuGet package declares Apache-2.0 for packaging. The binary release archive
also supplies a packaging MIT license in its top-level LICENSE. Both notices
are retained alongside the individual PDFium dependency licenses. The app uses
the NuGet binary; the release archive is used to obtain the matching notices.
