# Changelog

## 0.1.1

- Automatically match the typing box to the selected text's font family, size,
  bold weight, and italic style when that font is available in Windows.
- Re-typeset edits with the original PDF font instead of blocking ordinary
  Word-generated text with explicit letter spacing.
- Allow longer replacements to use adjacent empty space while retaining font size.
- Ignore zero-area trailing text objects in collision checks.
- Preserve untouched glyph positions when PDFium rewrites character/word-spacing
  operators. Keep the existing pixel comparisons outside the selected text and
  on all other pages.
- Add regression coverage for custom spacing, longer replacements, real visible
  collisions, empty trailing text objects, and short-word spacing preservation.

## 0.1.0

- Initial Windows developer preview with direct PDF text replacement, preview,
  apply, undo/redo, and safe save-a-copy.
