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

    public long FileSize => _bytes?.LongLength ?? 0;

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
        return new PdfPage(_handle, page);
    }

    public PdfMetadata GetMetadata()
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfDocument));

        int versionRaw = 0;
        lock (PdfNative.SyncRoot)
        {
            fpdfview.FPDF_GetFileVersion(_handle, ref versionRaw);
        }
        var versionStr = versionRaw > 0
            ? $"{versionRaw / 10}.{versionRaw % 10}"
            : "Unknown";

        return new PdfMetadata(
            Title: ReadMetaText("Title"),
            Author: ReadMetaText("Author"),
            Subject: ReadMetaText("Subject"),
            Keywords: ReadMetaText("Keywords"),
            Creator: ReadMetaText("Creator"),
            Producer: ReadMetaText("Producer"),
            CreationDate: ParsePdfDate(ReadMetaText("CreationDate")),
            ModificationDate: ParsePdfDate(ReadMetaText("ModDate")),
            PdfVersion: versionStr);
    }

    private string? ReadMetaText(string tag)
    {
        if (_handle is null) return null;

        ulong size;
        lock (PdfNative.SyncRoot)
        {
            size = fpdf_doc.FPDF_GetMetaText(_handle, tag, IntPtr.Zero, 0);
        }
        if (size <= 2) return null; // null terminator only

        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            lock (PdfNative.SyncRoot)
            {
                fpdf_doc.FPDF_GetMetaText(_handle, tag, buf, size);
            }
            // PDFium writes UTF-16LE with a trailing null wchar.
            var text = Marshal.PtrToStringUni(buf, (int)(size / 2) - 1);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static DateTimeOffset? ParsePdfDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        // PDF date format: D:YYYYMMDDHHmmSSOHH'mm' (O = +, -, or Z; trailing parts optional)
        var s = raw.StartsWith("D:") ? raw[2..] : raw;
        if (s.Length < 4) return null;

        try
        {
            int year = int.Parse(s[..4]);
            int month = s.Length >= 6 ? int.Parse(s.Substring(4, 2)) : 1;
            int day = s.Length >= 8 ? int.Parse(s.Substring(6, 2)) : 1;
            int hour = s.Length >= 10 ? int.Parse(s.Substring(8, 2)) : 0;
            int min = s.Length >= 12 ? int.Parse(s.Substring(10, 2)) : 0;
            int sec = s.Length >= 14 ? int.Parse(s.Substring(12, 2)) : 0;

            var offset = TimeSpan.Zero;
            if (s.Length >= 15)
            {
                var tz = s[14];
                if (tz == 'Z') offset = TimeSpan.Zero;
                else if ((tz == '+' || tz == '-') && s.Length >= 17)
                {
                    int oh = int.Parse(s.Substring(15, 2));
                    int om = 0;
                    // Format uses apostrophes: +HH'mm'
                    var rest = s[17..].Replace("'", "");
                    if (rest.Length >= 2) int.TryParse(rest[..2], out om);
                    var span = new TimeSpan(oh, om, 0);
                    offset = tz == '-' ? -span : span;
                }
            }

            return new DateTimeOffset(year, month, day, hour, min, sec, offset);
        }
        catch
        {
            return null;
        }
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
