namespace BarePDF.Pdfium;

/// <summary>
/// A rectangle in PDF coordinate space (origin at lower-left, y-axis up).
/// Top is the larger y, Bottom the smaller — opposite of WPF.
/// </summary>
public readonly record struct PdfRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;
}
