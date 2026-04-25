using PDFiumCore;

namespace BarePDF.Pdfium;

internal static class PdfNative
{
    internal static readonly object SyncRoot = new();
    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (SyncRoot)
        {
            if (_initialized) return;
            fpdfview.FPDF_InitLibrary();
            _initialized = true;
        }
    }
}
