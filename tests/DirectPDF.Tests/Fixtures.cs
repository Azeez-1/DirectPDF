using System.Text;

namespace DirectPDF.Tests;

// These fixtures are authored here, not downloaded user documents. Hand-written
// streams exercise PDF encodings and positioning independently of our editor.
internal static class Fixtures
{
    internal static byte[] Simple(string text = "Invoice number 12345", string extra = "", string pageOptions = "", string catalogOptions = "") => Build([
        $"<< /Type /Catalog /Pages 2 0 R {catalogOptions} >>",
        "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R {pageOptions} >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        Stream($"q 0.92 0.95 0.98 rg 40 650 530 100 re f Q\nBT /F1 18 Tf 0.12 0.18 0.25 rg 1 0 0 1 60 710 Tm ({text}) Tj ET\nBT /F1 12 Tf 1 0 0 1 60 670 Tm (Keep this line unchanged.) Tj ET\n{extra}"),
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 7 0 R >>",
        Stream("BT /F1 24 Tf 1 0 0 1 60 710 Tm (Second page is unchanged.) Tj ET")
    ]);

    internal static byte[] Kerned() => Build([
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        Stream("BT /F1 24 Tf 1 0 0 1 60 700 Tm [(Hello) -500 (world)] TJ ET")
    ]);

    internal static byte[] NoText() => Build([
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
        Stream("0.9 0.9 0.9 rg 50 50 500 700 re f")
    ]);

    internal static byte[] Demo() => Build([
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 7 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        Stream("q 0.06 0.35 0.32 rg 0 750 612 42 re f Q\n" +
            Text("DIRECTPDF / SAMPLE DOCUMENT", "F2", 10, 48, 766, "1 1 1") +
            Text("A small change. The same PDF.", "F2", 26, 48, 690, "0.08 0.16 0.22") +
            Text("Click a line of text to try your first edit.", "F1", 13, 48, 658) +
            "q 0.94 0.96 0.96 rg 48 430 516 180 re f Q\n" +
            Text("PROJECT DETAILS", "F2", 10, 68, 582, "0.06 0.35 0.32") +
            Text("Project: The October newsletter", "F1", 17, 68, 543) +
            Text("Status: Waiting for final review", "F1", 17, 68, 506) +
            Text("Reference: 2026-12345", "F1", 17, 68, 469) +
            Text("TRY THIS", "F2", 10, 48, 375, "0.06 0.35 0.32") +
            Text("Change the status to: Status: Ready", "F1", 13, 48, 347) +
            Text("Preview the result, apply it, then save an edited copy.", "F1", 13, 48, 322) +
            Text("Edits keep the original font, size, colour, and baseline.", "F1", 12, 48, 253) +
            Text("Use shorter text when a replacement would not fit.", "F1", 12, 48, 230) +
            Text("This sample was created for DirectPDF. All editing works offline.", "F1", 10, 48, 55)),
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 8 0 R >>",
        Stream(Text("A second page, preserved.", "F2", 24, 48, 690) +
            Text("Changes on the first page leave this page intact.", "F1", 14, 48, 645) +
            Text("DirectPDF validates every page before accepting an edit.", "F1", 13, 48, 612))
    ]);

    private static string Text(string text, string font, int size, int x, int y, string colour = "0.15 0.22 0.28") =>
        $"BT /{font} {size} Tf {colour} rg 1 0 0 1 {x} {y} Tm ({text}) Tj ET\n";
    private static string Stream(string data) => $"<< /Length {Encoding.ASCII.GetByteCount(data)} >>\nstream\n{data}\nendstream";
    private static byte[] Build(string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.7\n% DirectPDF test fixture\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
