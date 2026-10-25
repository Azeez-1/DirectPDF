namespace DirectPDF.Core;

public sealed class PdfEditException : Exception
{
    public PdfEditException(string message, Exception? inner = null) : base(message, inner) { }
}

public readonly record struct PdfRect(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;
    public PdfRect Union(PdfRect other) => new(Math.Min(Left, other.Left), Math.Min(Bottom, other.Bottom), Math.Max(Right, other.Right), Math.Max(Top, other.Top));
    public bool Intersects(PdfRect other) => Left < other.Right && Right > other.Left && Bottom < other.Top && Top > other.Bottom;
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public sealed record PageBitmap(int Width, int Height, int Stride, byte[] Pixels);
public sealed record TextSegment(int ObjectIndex, string Text, string Font, float FontSize, PdfRect Bounds, PixelRect DisplayBounds, string? Restriction)
{
    public string FontFamily { get; init; } = Font;
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public bool CanEdit => Restriction == null;
    public string Detail => CanEdit ? $"{Font} · {FontSize:0.#} pt" : Restriction!;
}
public sealed record PageView(int PageIndex, int PageCount, PageBitmap Bitmap, IReadOnlyList<TextSegment> Segments, string? Restriction);
public sealed class EditPreview
{
    internal byte[] Bytes { get; }
    internal long Version { get; }
    internal Guid Owner { get; }
    public PageView View { get; }
    internal EditPreview(byte[] bytes, long version, Guid owner, PageView view) => (Bytes, Version, Owner, View) = (bytes, version, owner, view);
}
