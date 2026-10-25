using System.Runtime.InteropServices;
using DirectPDF.Core;

namespace DirectPDF.Tests;

internal static class EmbeddedFixture
{
    [DllImport("pdfium")] private static extern IntPtr FPDF_CreateNewDocument();
    [DllImport("pdfium")] private static extern IntPtr FPDFPage_New(IntPtr doc, int index, double width, double height);
    [DllImport("pdfium")] private static extern IntPtr FPDFText_LoadFont(IntPtr doc, byte[] data, uint size, int type, int cid);
    [DllImport("pdfium")] private static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr doc, IntPtr font, float size);
    [DllImport("pdfium")] private static extern void FPDFPageObj_Transform(IntPtr obj, double a, double b, double c, double d, double e, double f);
    [DllImport("pdfium")] private static extern void FPDFPage_InsertObject(IntPtr page, IntPtr obj);
    [DllImport("pdfium")] private static extern void FPDFFont_Close(IntPtr font);

    internal static byte[] Create()
    {
        lock (Native.Gate)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
            var doc = FPDF_CreateNewDocument();
            if (doc == IntPtr.Zero) throw new Exception("Cannot create fixture document");
            try
            {
                var font = FPDFText_LoadFont(doc, bytes, (uint)bytes.Length, 2, 1);
                if (font == IntPtr.Zero) throw new Exception("Cannot load fixture font");
                try
                {
                    var page = FPDFPage_New(doc, 0, 612, 792);
                    try
                    {
                        var text = FPDFPageObj_CreateTextObj(doc, font, 18);
                        if (Native.FPDFText_SetText(text, "Invoice number 12345") == 0) throw new Exception("Cannot set fixture text");
                        FPDFPageObj_Transform(text, 1, 0, 0, 1, 60, 700);
                        FPDFPage_InsertObject(page, text);
                        if (Native.FPDFPage_GenerateContent(page) == 0) throw new Exception("Cannot generate fixture content");
                        using var output = new MemoryStream();
                        Native.WriteBlock callback = (_, data, size) =>
                        {
                            var buffer = new byte[size];
                            Marshal.Copy(data, buffer, 0, buffer.Length);
                            output.Write(buffer);
                            return 1;
                        };
                        var writer = new Native.FileWrite { Version = 1, Callback = Marshal.GetFunctionPointerForDelegate(callback) };
                        if (Native.FPDF_SaveAsCopy(doc, ref writer, 2) == 0) throw new Exception("Cannot save fixture");
                        GC.KeepAlive(callback);
                        return output.ToArray();
                    }
                    finally { Native.FPDF_ClosePage(page); }
                }
                finally { FPDFFont_Close(font); }
            }
            finally { Native.FPDF_CloseDocument(doc); }
        }
    }
}
