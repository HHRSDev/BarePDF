using System.IO;
using System.Runtime.InteropServices;
using PDFiumCore;

namespace BarePDF.Pdfium;

public sealed class PdfDocument : IDisposable
{
    private FpdfDocumentT? _handle;
    private GCHandle _bytesPin;
    private byte[]? _bytes;

    private PdfDocument(FpdfDocumentT handle, byte[] bytes, GCHandle pin, int pageCount)
    {
        _handle = handle;
        _bytes = bytes;
        _bytesPin = pin;
        PageCount = pageCount;
    }

    public int PageCount { get; }

    public static PdfDocument Open(string path, string? password = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path)) throw new FileNotFoundException("PDF not found", path);
        return Load(File.ReadAllBytes(path), password);
    }

    public static PdfDocument Load(byte[] bytes, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) throw new ArgumentException("PDF buffer is empty", nameof(bytes));

        PdfNative.EnsureInitialized();

        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            FpdfDocumentT? handle;
            int pageCount;
            ulong err = 0;
            lock (PdfNative.SyncRoot)
            {
                handle = fpdfview.FPDF_LoadMemDocument(pin.AddrOfPinnedObject(), bytes.Length, password);
                if (handle is null)
                {
                    err = fpdfview.FPDF_GetLastError();
                }
                pageCount = handle is null ? 0 : fpdfview.FPDF_GetPageCount(handle);
            }
            if (handle is null)
            {
                pin.Free();
                throw new PdfException(MapError(err), err);
            }
            return new PdfDocument(handle, bytes, pin, pageCount);
        }
        catch
        {
            if (pin.IsAllocated) pin.Free();
            throw;
        }
    }

    public PdfPage GetPage(int index)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfDocument));
        if (index < 0 || index >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        FpdfPageT? page;
        lock (PdfNative.SyncRoot)
        {
            page = fpdfview.FPDF_LoadPage(_handle, index);
        }
        if (page is null) throw new PdfException("Failed to load page", 0);
        return new PdfPage(page);
    }

    public void Dispose()
    {
        if (_handle is null) return;
        lock (PdfNative.SyncRoot)
        {
            if (_handle is null) return;
            fpdfview.FPDF_CloseDocument(_handle);
            _handle = null;
        }
        if (_bytesPin.IsAllocated) _bytesPin.Free();
        _bytes = null;
    }

    private static string MapError(ulong err) => err switch
    {
        2 => "File not found or could not be opened",
        3 => "Not a PDF file or corrupted",
        4 => "Password required or incorrect",
        5 => "Unsupported security scheme",
        6 => "Page not found or content error",
        _ => "Unknown PDF error",
    };
}
