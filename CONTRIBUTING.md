# Contributing

Use Windows x64 and the .NET 10 SDK. Run `scripts/build.ps1` and
`scripts/test.ps1` before submitting a change. Native integration tests must pass;
a build alone does not exercise PDF editing.

The repository deliberately includes the self-contained app in `app/win-x64`
so end users can clone and run without installing a development toolchain.
After changing app behaviour, publish the new standalone build, refresh this
bundle with `scripts/update-portable.ps1`, and commit its updated manifest too.
`scripts/test-portable.ps1` checks its version, file hashes, and startup without
calling a system dotnet installation. Do not use Git LFS pointers or a download
script in place of the bundled app: the launcher must work offline after cloning.

Keep the PDF engine independent of WPF. Add fixtures for meaningful changes to
font handling, text placement, save behaviour, or document eligibility. Prefer
small, authored PDFs whose expected behaviour is explicit. Do not commit private
documents, generated font-containing test artifacts, caches, or binaries from
your local build.

New editing support must establish that saved text is correct and unrelated
content survives. Do not silently substitute fonts, flatten pages, or hide old
text behind new objects. Keep limitations visible in the interface and README.

When updating PDFium, update the pinned version, package lock files, matching
license bundle, and provenance hash together. Run the full integration runner.
