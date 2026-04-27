using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFiumCore;

namespace BarePDF.Pdfium;

public sealed class PdfPage : IDisposable
{
    private FpdfPageT? _handle;

    internal PdfPage(FpdfPageT handle)
    {
        _handle = handle;
        WidthPoints = fpdfview.FPDF_GetPageWidthF(handle);
        HeightPoints = fpdfview.FPDF_GetPageHeightF(handle);
    }

    public double WidthPoints { get; }
    public double HeightPoints { get; }

    public PdfTextPage LoadTextPage()
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfPage));
        FpdfTextpageT? handle;
        int charCount;
        lock (PdfNative.SyncRoot)
        {
            handle = fpdf_text.FPDFTextLoadPage(_handle);
            charCount = handle is null ? 0 : fpdf_text.FPDFTextCountChars(handle);
        }
        if (handle is null) throw new PdfException("Failed to load text page", 0);
        return new PdfTextPage(handle, charCount);
    }

    public BitmapSource Render(double dpi)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfPage));
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

        var pixelWidth = Math.Max(1, (int)Math.Round(WidthPoints * dpi / 72.0));
        var pixelHeight = Math.Max(1, (int)Math.Round(HeightPoints * dpi / 72.0));

        lock (PdfNative.SyncRoot)
        {
            var bitmap = fpdfview.FPDFBitmapCreateEx(pixelWidth, pixelHeight, 4, IntPtr.Zero, 0);
            if (bitmap is null) throw new PdfException("Failed to allocate PDFium bitmap", 0);
            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFFUL);
                fpdfview.FPDF_RenderPageBitmap(bitmap, _handle, 0, 0, pixelWidth, pixelHeight, 0, 0);

                var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var bufferSize = stride * pixelHeight;

                var source = BitmapSource.Create(
                    pixelWidth, pixelHeight,
                    dpi, dpi,
                    PixelFormats.Bgra32,
                    null,
                    buffer, bufferSize, stride);
                source.Freeze();
                return source;
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
    }

    public void Dispose()
    {
        if (_handle is null) return;
        lock (PdfNative.SyncRoot)
        {
            if (_handle is null) return;
            fpdfview.FPDF_ClosePage(_handle);
            _handle = null;
        }
    }
}
