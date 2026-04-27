using System.Runtime.InteropServices;
using PDFiumCore;

namespace BarePDF.Pdfium;

public sealed class PdfTextPage : IDisposable
{
    private FpdfTextpageT? _handle;

    internal PdfTextPage(FpdfTextpageT handle, int charCount)
    {
        _handle = handle;
        CharCount = charCount;
    }

    public int CharCount { get; }

    public PdfRect GetCharBox(int index)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfTextPage));
        if (index < 0 || index >= CharCount) throw new ArgumentOutOfRangeException(nameof(index));

        double left = 0, right = 0, bottom = 0, top = 0;
        lock (PdfNative.SyncRoot)
        {
            fpdf_text.FPDFTextGetCharBox(_handle, index, ref left, ref right, ref bottom, ref top);
        }
        return new PdfRect(left, top, right, bottom);
    }

    public char GetUnicode(int index)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfTextPage));
        if (index < 0 || index >= CharCount) throw new ArgumentOutOfRangeException(nameof(index));

        uint code;
        lock (PdfNative.SyncRoot)
        {
            code = fpdf_text.FPDFTextGetUnicode(_handle, index);
        }
        return code <= char.MaxValue ? (char)code : '�';
    }

    public int GetCharIndexAtPoint(double pdfX, double pdfY, double xTolerance = 5.0, double yTolerance = 5.0)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfTextPage));
        lock (PdfNative.SyncRoot)
        {
            return fpdf_text.FPDFTextGetCharIndexAtPos(_handle, pdfX, pdfY, xTolerance, yTolerance);
        }
    }

    public string ExtractText(int start, int count)
    {
        if (_handle is null) throw new ObjectDisposedException(nameof(PdfTextPage));
        if (count <= 0) return string.Empty;
        if (start < 0 || start >= CharCount) return string.Empty;
        if (start + count > CharCount) count = CharCount - start;

        var buffer = new ushort[count + 1];
        int written;
        lock (PdfNative.SyncRoot)
        {
            written = fpdf_text.FPDFTextGetText(_handle, start, count, ref buffer[0]);
        }
        var len = Math.Max(0, written - 1);
        var chars = MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, len));
        return new string(chars);
    }

    public void Dispose()
    {
        if (_handle is null) return;
        lock (PdfNative.SyncRoot)
        {
            if (_handle is null) return;
            fpdf_text.FPDFTextClosePage(_handle);
            _handle = null;
        }
    }
}
