using System.Runtime.InteropServices;
using System.Text;

namespace DirectPDF.Core;

// All access is serialized by PdfEditor.Native.Gate. The pinned input must
// outlive the native document, including during save callbacks.
internal sealed class PdfDocument : IDisposable
{
    private GCHandle _input;
    internal IntPtr Handle { get; private set; }
    internal int PageCount => Native.FPDF_GetPageCount(Handle);

    internal PdfDocument(byte[] bytes)
    {
        Native.EnsureInitialized();
        _input = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        Handle = Native.FPDF_LoadMemDocument64(_input.AddrOfPinnedObject(), (nuint)bytes.Length, null);
        if (Handle == IntPtr.Zero)
        {
            var error = Native.FPDF_GetLastError();
            _input.Free();
            throw new PdfEditException(error == 4
                ? "This PDF requires a password. Password-protected documents are not supported in this version."
                : "The file could not be opened as a PDF. It may be damaged or use an unsupported format.");
        }
    }

    internal PdfPage Page(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new PdfPage(Native.FPDF_LoadPage(Handle, index));
    }

    internal string? EditingRestriction =>
        Native.FPDF_GetSecurityHandlerRevision(Handle) >= 0 ? "Encrypted PDFs are view-only in this version." :
        Native.FPDF_GetSignatureCount(Handle) > 0 ? "Digitally signed PDFs are view-only to preserve their signatures." :
        Native.FPDF_GetFormType(Handle) != 0 ? "PDFs with interactive forms are view-only in this version." : null;

    internal byte[] Save()
    {
        using var stream = new MemoryStream();
        Exception? failure = null;
        Native.WriteBlock callback = (_, data, size) =>
        {
            try
            {
                var block = new byte[checked((int)size)];
                Marshal.Copy(data, block, 0, block.Length);
                stream.Write(block);
                return 1;
            }
            catch (Exception ex) { failure = ex; return 0; }
        };
        var writer = new Native.FileWrite { Version = 1, Callback = Marshal.GetFunctionPointerForDelegate(callback) };
        var success = Native.FPDF_SaveAsCopy(Handle, ref writer, 2); // full rewrite, no incremental history
        GC.KeepAlive(callback);
        if (success == 0 || failure != null) throw new PdfEditException("PDFium could not save the document.", failure);
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { Native.FPDF_CloseDocument(Handle); Handle = IntPtr.Zero; }
        if (_input.IsAllocated) _input.Free();
    }
}

internal sealed class PdfPage : IDisposable
{
    internal IntPtr Handle { get; }
    internal double Width => Native.FPDF_GetPageWidthF(Handle);
    internal double Height => Native.FPDF_GetPageHeightF(Handle);
    internal PdfPage(IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new PdfEditException("This page could not be read.");
        Handle = handle;
    }

    internal string Text()
    {
        var textPage = Native.FPDFText_LoadPage(Handle);
        if (textPage == IntPtr.Zero) throw new PdfEditException("The page text could not be read.");
        try
        {
            var count = Native.FPDFText_CountChars(textPage);
            if (count <= 0) return "";
            var buffer = new byte[checked((count + 1) * 2)];
            var written = Native.FPDFText_GetText(textPage, 0, count, buffer);
            return written <= 0 ? "" : Encoding.Unicode.GetString(buffer, 0, (written - 1) * 2);
        }
        finally { Native.FPDFText_ClosePage(textPage); }
    }

    internal PageBitmap Render(double scale)
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
            throw new PdfEditException("This page has invalid dimensions.");
        scale = Math.Clamp(scale, .1, 4);
        scale = Math.Min(scale, Math.Sqrt(16_000_000 / (Width * Height)));
        var width = Math.Max(1, checked((int)Math.Ceiling(Width * scale)));
        var height = Math.Max(1, checked((int)Math.Ceiling(Height * scale)));
        if (width > 32768 || height > 32768) throw new PdfEditException("This page is too large to render safely.");
        var bitmap = Native.FPDFBitmap_Create(width, height, 1);
        if (bitmap == IntPtr.Zero) throw new PdfEditException("Not enough memory to display this page.");
        try
        {
            Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff);
            Native.FPDF_RenderPageBitmap(bitmap, Handle, 0, 0, width, height, 0, 1); // annotations, no JS/actions
            var stride = Native.FPDFBitmap_GetStride(bitmap);
            var pixels = new byte[checked(stride * height)];
            Marshal.Copy(Native.FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
            return new PageBitmap(width, height, stride, pixels);
        }
        finally { Native.FPDFBitmap_Destroy(bitmap); }
    }

    internal PixelRect ToPixels(PdfRect rect, int width, int height)
    {
        Native.FPDF_PageToDevice(Handle, 0, 0, width, height, 0, rect.Left, rect.Top, out var x1, out var y1);
        Native.FPDF_PageToDevice(Handle, 0, 0, width, height, 0, rect.Right, rect.Bottom, out var x2, out var y2);
        return new PixelRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    public void Dispose() => Native.FPDF_ClosePage(Handle);
}
