using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFiumCore;

namespace BarePDF.Pdfium;

public sealed class PdfPage : IDisposable
{
    private FpdfPageT? _handle;
    private readonly FpdfDocumentT _documentHandle;

    internal PdfPage(FpdfDocumentT documentHandle, FpdfPageT handle)
    {
        _documentHandle = documentHandle;
        _handle = handle;
        WidthPoints = fpdfview.FPDF_GetPageWidthF(handle);
        HeightPoints = fpdfview.FPDF_GetPageHeightF(handle);
    }

    public double WidthPoints { get; }
    public double HeightPoints { get; }

    public PdfLinkTarget? GetLinkAtPoint(double pdfX, double pdfY)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfPage));

        lock (PdfNative.SyncRoot)
        {
            var link = fpdf_doc.FPDFLinkGetLinkAtPoint(_handle, pdfX, pdfY);
            if (link is null) return null;

            // Try direct destination first (intra-doc GoTo).
            var dest = fpdf_doc.FPDFLinkGetDest(_documentHandle, link);
            if (dest is not null)
            {
                var pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(_documentHandle, dest);
                if (pageIndex >= 0) return new PdfGoToLinkTarget(pageIndex);
            }

            // Otherwise read the action.
            var action = fpdf_doc.FPDFLinkGetAction(link);
            if (action is null) return null;

            var actionType = fpdf_doc.FPDFActionGetType(action);
            // 1 = GoTo (intra-doc), 3 = URI
            if (actionType == 1)
            {
                var actionDest = fpdf_doc.FPDFActionGetDest(_documentHandle, action);
                if (actionDest is not null)
                {
                    var pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(_documentHandle, actionDest);
                    if (pageIndex >= 0) return new PdfGoToLinkTarget(pageIndex);
                }
            }
            else if (actionType == 3)
            {
                var requiredLen = fpdf_doc.FPDFActionGetURIPath(_documentHandle, action, IntPtr.Zero, 0);
                if (requiredLen <= 1) return null;
                var buffer = new byte[requiredLen];
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    fpdf_doc.FPDFActionGetURIPath(_documentHandle, action, pin.AddrOfPinnedObject(), requiredLen);
                }
                finally
                {
                    pin.Free();
                }
                // PDFium returns ASCII/Latin-1 with a trailing null
                var urlLen = (int)requiredLen - 1;
                if (urlLen <= 0) return null;
                var url = Encoding.ASCII.GetString(buffer, 0, urlLen);
                return new PdfUriLinkTarget(url);
            }

            return null;
        }
    }

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
                // flags=1 → FPDF_ANNOT: paint annotations (highlights, free-text, sticky notes…) into the bitmap
                fpdfview.FPDF_RenderPageBitmap(bitmap, _handle, 0, 0, pixelWidth, pixelHeight, 0, 1);

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
