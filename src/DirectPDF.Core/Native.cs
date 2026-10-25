using System.Runtime.InteropServices;

namespace DirectPDF.Core;

// PDFium uses Windows unsigned long (32 bits), size_t (pointer-sized), and
// UTF-16LE for text. Keep these declarations aligned with the pinned headers.
internal static class Native
{
    private const string Dll = "pdfium";
    internal static readonly object Gate = new();
    static Native() => FPDF_InitLibrary();
    internal static void EnsureInitialized() { }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Matrix { public float A, B, C, D, E, F; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int WriteBlock(IntPtr self, IntPtr data, uint size);
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileWrite { public int Version; public IntPtr Callback; }

    [DllImport(Dll)] private static extern void FPDF_InitLibrary();
    [DllImport(Dll)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Dll)] internal static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Dll)] internal static extern uint FPDF_GetLastError();
    [DllImport(Dll)] internal static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Dll)] internal static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);
    [DllImport(Dll)] internal static extern int FPDF_GetSignatureCount(IntPtr document);
    [DllImport(Dll)] internal static extern int FPDF_GetFormType(IntPtr document);
    [DllImport(Dll)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Dll)] internal static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll)] internal static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll)] internal static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll)] internal static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(Dll)] internal static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport(Dll)] internal static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
    [DllImport(Dll)] internal static extern int FPDFPageObj_GetType(IntPtr obj);
    [DllImport(Dll)] internal static extern int FPDFPageObj_GetBounds(IntPtr obj, out float left, out float bottom, out float right, out float top);
    [DllImport(Dll)] internal static extern int FPDFPageObj_GetMatrix(IntPtr obj, out Matrix matrix);
    [DllImport(Dll)] internal static extern int FPDFPageObj_GetFillColor(IntPtr obj, out uint red, out uint green, out uint blue, out uint alpha);
    [DllImport(Dll)] internal static extern int FPDFPageObj_HasTransparency(IntPtr obj);
    [DllImport(Dll)] internal static extern IntPtr FPDFPageObj_GetClipPath(IntPtr obj);
    [DllImport(Dll)] internal static extern int FPDFClipPath_CountPaths(IntPtr clipPath);
    [DllImport(Dll)] internal static extern int FPDFTextObj_GetTextRenderMode(IntPtr obj);
    [DllImport(Dll)] internal static extern int FPDFTextObj_GetFontSize(IntPtr obj, out float size);
    [DllImport(Dll)] internal static extern IntPtr FPDFTextObj_GetFont(IntPtr obj);
    [DllImport(Dll)] internal static extern nuint FPDFFont_GetBaseFontName(IntPtr font, byte[]? buffer, nuint length);
    [DllImport(Dll)] internal static extern nuint FPDFFont_GetFamilyName(IntPtr font, byte[]? buffer, nuint length);
    [DllImport(Dll)] internal static extern int FPDFFont_GetWeight(IntPtr font);
    [DllImport(Dll)] internal static extern int FPDFFont_GetItalicAngle(IntPtr font, out float angle);
    [DllImport(Dll)] internal static extern int FPDFFont_GetIsEmbedded(IntPtr font);
    [DllImport(Dll)] internal static extern uint FPDFTextObj_GetText(IntPtr obj, IntPtr textPage, byte[]? buffer, uint length);
    [DllImport(Dll)] internal static extern int FPDFText_SetText(IntPtr obj, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport(Dll)] internal static extern int FPDFText_SetPositions(IntPtr obj, float[] positions, nuint count);
    [DllImport(Dll)] internal static extern int FPDFPage_RemoveObject(IntPtr page, IntPtr obj);
    [DllImport(Dll)] internal static extern void FPDFPageObj_Destroy(IntPtr obj);
    [DllImport(Dll)] internal static extern int FPDFPage_GenerateContent(IntPtr page);
    [DllImport(Dll)] internal static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Dll)] internal static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Dll)] internal static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Dll)] internal static extern int FPDFText_GetText(IntPtr textPage, int start, int count, byte[] buffer);
    [DllImport(Dll)] internal static extern int FPDFText_HasUnicodeMapError(IntPtr textPage, int index);
    [DllImport(Dll)] internal static extern IntPtr FPDFText_GetTextObject(IntPtr textPage, int index);
    [DllImport(Dll)] internal static extern int FPDFText_IsGenerated(IntPtr textPage, int index);
    [DllImport(Dll)] internal static extern int FPDFText_GetCharOrigin(IntPtr textPage, int index, out double x, out double y);
    [DllImport(Dll)] internal static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int width, int height, int rotate, double pageX, double pageY, out int deviceX, out int deviceY);
    [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll)] internal static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll)] internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport(Dll)] internal static extern int FPDF_SaveAsCopy(IntPtr document, ref FileWrite writer, uint flags);
}
